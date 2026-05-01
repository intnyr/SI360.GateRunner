using SI360.GateRunner.Models;

namespace SI360.GateRunner.Services;

public sealed record DecisionPolicyResult(
    DeployDecision Decision,
    string PolicyName,
    string PolicyVersion,
    string Rationale,
    IReadOnlyList<QualityIssue> Impacts);

public interface IDecisionPolicy
{
    string Name { get; }
    string Version { get; }
    DecisionPolicyResult Decide(RunSummary summary);
}

public sealed class DecisionPolicy : IDecisionPolicy
{
    public string Name => "AllGatesAndScorecard";
    public string Version => "2026.05.01";

    public DecisionPolicyResult Decide(RunSummary summary)
    {
        QualityIssueAggregator.RefreshDerivedIssues(summary);

        var hardErrors = summary.QualityIssues
            .Where(i => i.Severity == QualityIssueSeverity.Error)
            .ToList();
        if (hardErrors.Count > 0)
        {
            return Result(
                DeployDecision.NoGo,
                $"NO-GO: {hardErrors.Count} error issue(s) fail the quality gate: {string.Join(", ", hardErrors.Select(i => i.Id))}.",
                hardErrors);
        }

        if (summary.GateResults.Count == 0)
        {
            return Result(DeployDecision.NoGo, "NO-GO: no gate results were available.");
        }

        var redOrError = summary.GateResults
            .Where(g => g.Status is GateStatus.Red or GateStatus.Error)
            .Select(g => g.Definition.Id)
            .ToList();
        if (redOrError.Count > 0)
        {
            return Result(
                DeployDecision.NoGo,
                $"NO-GO: {redOrError.Count} gate(s) are red/error: {string.Join(", ", redOrError)}.");
        }

        var warnings = summary.QualityIssues
            .Where(i => i.Severity == QualityIssueSeverity.Warning)
            .ToList();
        if (warnings.Count > 0)
        {
            return Result(
                DeployDecision.Hold,
                $"HOLD: {warnings.Count} warning issue(s) apply {summary.Scorecard.QualityPenalty:0.00} point(s) of quality penalty: {string.Join(", ", warnings.Select(i => i.Id))}.",
                warnings);
        }

        var score = summary.Scorecard.OverallScore;
        if (score < 85)
        {
            return Result(
                DeployDecision.NoGo,
                $"NO-GO: score {score:0.00} is below the HOLD threshold of 85.00.");
        }

        var yellow = summary.GateResults
            .Where(g => g.Status == GateStatus.Yellow)
            .Select(g => g.Definition.Id)
            .ToList();
        if (yellow.Count > 0)
        {
            return Result(
                DeployDecision.Hold,
                $"HOLD: score {score:0.00} passed, but {yellow.Count} gate(s) are yellow: {string.Join(", ", yellow)}.");
        }

        if (score < 95)
        {
            return Result(
                DeployDecision.Hold,
                $"HOLD: score {score:0.00} is below the GO threshold of 95.00.");
        }

        return Result(
            DeployDecision.Go,
            $"GO: all gates are green and score {score:0.00} meets the GO threshold.");
    }

    private DecisionPolicyResult Result(
        DeployDecision decision,
        string rationale,
        IReadOnlyList<QualityIssue>? impacts = null) =>
        new(decision, Name, Version, rationale, impacts ?? Array.Empty<QualityIssue>());
}
