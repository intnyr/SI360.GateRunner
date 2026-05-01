using SI360.GateRunner.Models;

namespace SI360.GateRunner.Services;

public static class QualityIssueAggregator
{
    private const double WarningPenalty = 2.0;

    public static void RefreshDerivedIssues(RunSummary summary)
    {
        foreach (var error in summary.BuildErrors)
        {
            if (summary.QualityIssues.Any(i =>
                    i.Source == QualityIssueSource.Build &&
                    i.Severity == QualityIssueSeverity.Error &&
                    string.Equals(i.Code, error.Code, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(i.Message, error.Message, StringComparison.Ordinal)))
                continue;
            var id = $"build:error:{error.Code}:{error.FilePath}:{error.Line}:{error.Column}";
            if (summary.QualityIssues.Any(i => i.Id == id))
                continue;
            summary.QualityIssues.Add(new QualityIssue(
                id,
                QualityIssueSeverity.Error,
                QualityIssueSource.Build,
                $"{error.FilePath}:{error.Line}",
                error.Code,
                error.Message,
                summary.Scorecard.BaseOverallScore,
                "NO-GO: build error fails the quality gate."));
        }

        RemoveDerivedIssues(summary);

        foreach (var warning in summary.GateCatalogWarnings)
        {
            summary.QualityIssues.Add(new QualityIssue(
                $"catalog:{warning.Code}",
                QualityIssueSeverity.Warning,
                QualityIssueSource.GateCatalog,
                "GateCatalog",
                warning.Code,
                warning.Message,
                WarningPenalty,
                "HOLD: unresolved gate catalog warning requires release-owner review."));
        }

        foreach (var issue in summary.DeploymentMetadata.Issues)
        {
            var severity = string.Equals(issue.Severity, "Error", StringComparison.OrdinalIgnoreCase)
                ? QualityIssueSeverity.Error
                : QualityIssueSeverity.Warning;
            summary.QualityIssues.Add(new QualityIssue(
                $"metadata:{issue.Code}:{issue.Field}",
                severity,
                QualityIssueSource.DeploymentMetadata,
                issue.Field,
                issue.Code,
                issue.Message,
                severity == QualityIssueSeverity.Error ? summary.Scorecard.BaseOverallScore : WarningPenalty,
                severity == QualityIssueSeverity.Error
                    ? "NO-GO: deployment metadata validation error."
                    : "HOLD: deployment metadata warning requires review."));
        }

        foreach (var probe in summary.SyntheticProbes.Where(p => p.Status is SyntheticProbeStatus.Failed or SyntheticProbeStatus.Error))
        {
            summary.QualityIssues.Add(new QualityIssue(
                $"probe:{probe.Id}",
                QualityIssueSeverity.Error,
                QualityIssueSource.SyntheticProbe,
                string.IsNullOrWhiteSpace(probe.Endpoint) ? probe.Name : probe.Endpoint,
                probe.Status.ToString(),
                $"{probe.Name}: {probe.Diagnostics}",
                summary.Scorecard.BaseOverallScore,
                "NO-GO: synthetic runtime probe failed or errored."));
        }

        if (summary.RuntimeReadiness == RuntimeReadinessDecision.NotReady)
        {
            summary.QualityIssues.Add(new QualityIssue(
                "runtime-readiness:not-ready",
                QualityIssueSeverity.Error,
                QualityIssueSource.RuntimeReadiness,
                "RuntimeReadiness",
                "RUNTIME_NOT_READY",
                summary.RuntimeReadinessRationale,
                summary.Scorecard.BaseOverallScore,
                "NO-GO: runtime readiness is NotReady."));
        }
        else if (summary.RuntimeReadiness == RuntimeReadinessDecision.Unknown)
        {
            summary.QualityIssues.Add(new QualityIssue(
                "runtime-readiness:unknown",
                QualityIssueSeverity.Warning,
                QualityIssueSource.RuntimeReadiness,
                "RuntimeReadiness",
                "RUNTIME_UNKNOWN",
                summary.RuntimeReadinessRationale,
                WarningPenalty,
                "HOLD: runtime readiness is Unknown."));
        }

        ApplyScorePenalty(summary);
    }

    public static void AddBuildIssues(
        RunSummary summary,
        IEnumerable<QualityIssue> buildIssues)
    {
        foreach (var issue in buildIssues)
        {
            if (summary.QualityIssues.Any(q => q.Id == issue.Id))
                continue;
            summary.QualityIssues.Add(issue);
            if (issue.Severity == QualityIssueSeverity.Error)
            {
                summary.BuildErrors.Add(new BuildError(
                    issue.SourceLocation,
                    ParseLine(issue.SourceLocation),
                    0,
                    issue.Code,
                    issue.Message));
            }
        }
    }

    public static void AddRestoreFailure(RunSummary summary, string sourcePath)
    {
        var issue = new QualityIssue(
            "build:RESTORE",
            QualityIssueSeverity.Error,
            QualityIssueSource.Build,
            sourcePath,
            "RESTORE",
            "dotnet restore failed.",
            summary.Scorecard.BaseOverallScore,
            "NO-GO: restore failed.");
        AddBuildIssues(summary, new[] { issue });
    }

    private static void RemoveDerivedIssues(RunSummary summary)
    {
        for (var i = summary.QualityIssues.Count - 1; i >= 0; i--)
        {
            if (summary.QualityIssues[i].Source is not QualityIssueSource.Build)
                summary.QualityIssues.RemoveAt(i);
        }
    }

    private static void ApplyScorePenalty(RunSummary summary)
    {
        var warningPenalty = summary.QualityIssues
            .Where(i => i.Severity == QualityIssueSeverity.Warning)
            .Sum(i => Math.Abs(i.ScoreImpact));
        summary.Scorecard.QualityPenalty = Math.Round(warningPenalty, 2);
    }

    private static int ParseLine(string sourceLocation)
    {
        var parts = sourceLocation.Split(':');
        return parts.Length > 1 && int.TryParse(parts[^1], out var line) ? line : 0;
    }
}
