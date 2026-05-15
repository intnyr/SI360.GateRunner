using SI360.GateRunner.Models;

namespace SI360.GateRunner.Services;

public sealed record GateRunRequest(bool BuildFirst = true);

public interface IGateRunOrchestrator
{
    Task<RunSummary> RunAsync(
        GateRunRequest request,
        IProgress<string>? log,
        CancellationToken cancellationToken);
}

public sealed class GateRunOrchestrator : IGateRunOrchestrator
{
    private readonly RunnerSettings _settings;
    private readonly DotnetTestRunner _testRunner;
    private readonly BuildErrorCollector _buildErrorCollector;
    private readonly TrxResultParser _parser;
    private readonly ScorecardAggregator _aggregator;
    private readonly IDecisionPolicy _decisionPolicy;
    private readonly IGateDiscoveryService _gateDiscovery;
    private readonly IReportWriter _reportWriter;
    private readonly IProcessRunner _processRunner;
    private readonly IDeploymentMetadataValidator _metadataValidator;
    private readonly ISyntheticProbeRunner _probeRunner;

    public GateRunOrchestrator(
        RunnerSettings settings,
        DotnetTestRunner testRunner,
        BuildErrorCollector buildErrorCollector,
        TrxResultParser parser,
        ScorecardAggregator aggregator,
        IDecisionPolicy decisionPolicy,
        IGateDiscoveryService gateDiscovery,
        IReportWriter reportWriter,
        IProcessRunner processRunner,
        IDeploymentMetadataValidator metadataValidator,
        ISyntheticProbeRunner probeRunner)
    {
        _settings = settings;
        _testRunner = testRunner;
        _buildErrorCollector = buildErrorCollector;
        _parser = parser;
        _aggregator = aggregator;
        _decisionPolicy = decisionPolicy;
        _gateDiscovery = gateDiscovery;
        _reportWriter = reportWriter;
        _processRunner = processRunner;
        _metadataValidator = metadataValidator;
        _probeRunner = probeRunner;
    }

    public async Task<RunSummary> RunAsync(
        GateRunRequest request,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTime.UtcNow;
        var runDir = Path.Combine(_settings.ResultsDirectory, $"GateRun_{startedAt:yyyyMMdd_HHmmss}Z");
        Directory.CreateDirectory(runDir);

        var summary = new RunSummary
        {
            StartedAt = startedAt,
            Environment = await RunEnvironmentCollector
                .CollectAsync(_settings, _processRunner, runDir, cancellationToken)
                .ConfigureAwait(false)
        };

        foreach (var warning in _gateDiscovery.Validate(_settings))
            summary.GateCatalogWarnings.Add(warning);

        await CollectRuntimeReadinessAsync(summary, cancellationToken).ConfigureAwait(false);

        if (request.BuildFirst)
        {
            if (!await EnsureSdkSupportsSolutionAsync(summary, runDir, log, cancellationToken).ConfigureAwait(false))
            {
                await FinalizeAsync(summary, startedAt).ConfigureAwait(false);
                return summary;
            }

            log?.Report("Restoring...");
            var restore = await _testRunner.RestoreAsync(log, cancellationToken, runDir).ConfigureAwait(false);
            if (restore.ExitCode != 0)
            {
                QualityIssueAggregator.AddRestoreFailure(summary, _settings.TestProjectPath);
                await FinalizeAsync(summary, startedAt).ConfigureAwait(false);
                return summary;
            }

            log?.Report("Building...");
            var (buildExit, issues) = await _buildErrorCollector.BuildAsync(log, cancellationToken, runDir).ConfigureAwait(false);
            QualityIssueAggregator.AddBuildIssues(summary, issues);
            if (buildExit != 0 || issues.Any(i => i.Severity == QualityIssueSeverity.Error))
            {
                await FinalizeAsync(summary, startedAt).ConfigureAwait(false);
                return summary;
            }
        }

        foreach (var gate in GateCatalog.All)
        {
            cancellationToken.ThrowIfCancellationRequested();
            log?.Report($"Running {gate.DisplayName}...");
            var started = DateTime.UtcNow;
            var (exit, trxPath, stdOut) = await _testRunner
                .RunGateAsync(gate.Id, gate.TestClassFilter, runDir, log, cancellationToken)
                .ConfigureAwait(false);

            var outcomes = _parser.Parse(trxPath, gate.Id);
            var result = new GateResult
            {
                Definition = gate,
                Duration = DateTime.UtcNow - started,
                TrxPath = trxPath,
                StdOut = stdOut,
                Passed = outcomes.Count(o => o.Status == TestStatus.Passed),
                Failed = outcomes.Count(o => o.Status == TestStatus.Failed),
                Skipped = outcomes.Count(o => o.Status == TestStatus.Skipped)
            };
            result.Outcomes.AddRange(outcomes);
            if (!File.Exists(trxPath))
                result.ErrorMessage = $"TRX file not found: {trxPath}";
            else if (result.Total == 0 && exit != 0)
                result.ErrorMessage = $"dotnet test exit {exit}; no TRX parsed.";
            result.ComputeStatus();
            summary.GateResults.Add(result);
        }

        await FinalizeAsync(summary, startedAt).ConfigureAwait(false);
        return summary;
    }

    private async Task FinalizeAsync(RunSummary summary, DateTime startedAt)
    {
        summary.Scorecard = _aggregator.Build(summary.GateResults);
        QualityIssueAggregator.RefreshDerivedIssues(summary);
        var decision = _decisionPolicy.Decide(summary);
        summary.Decision = decision.Decision;
        summary.DecisionPolicyName = decision.PolicyName;
        summary.DecisionPolicyVersion = decision.PolicyVersion;
        summary.DecisionRationale = decision.Rationale;
        summary.DecisionImpacts.Clear();
        summary.DecisionImpacts.AddRange(decision.Impacts);
        summary.Duration = DateTime.UtcNow - startedAt;
        var (md, json) = await _reportWriter.WriteAsync(summary, _settings.ResultsDirectory).ConfigureAwait(false);
        ReportRetentionPruner.Prune(_settings.ResultsDirectory, _settings.ReportRetentionDays, DateTimeOffset.UtcNow);
        summary.ReportMarkdownPath = md;
        summary.ReportJsonPath = json;
    }

    private static readonly Version SlnxMinimumSdk = new(9, 0, 200);

    private async Task<bool> EnsureSdkSupportsSolutionAsync(
        RunSummary summary,
        string runDir,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        var solutionPath = _settings.SolutionPath;
        if (string.IsNullOrWhiteSpace(solutionPath) ||
            !string.Equals(Path.GetExtension(solutionPath), ".slnx", StringComparison.OrdinalIgnoreCase))
            return true;

        var workingDir = Path.GetDirectoryName(solutionPath);
        if (string.IsNullOrWhiteSpace(workingDir) || !Directory.Exists(workingDir))
            return true;

        var probeDir = Path.Combine(runDir, "sdk-probe");
        var result = await _processRunner.RunAsync(
            new ProcessCommand(
                "dotnet",
                "--version",
                workingDir,
                TimeSpan.FromSeconds(15),
                probeDir,
                "sdk-probe"),
            log,
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StdOut))
        {
            QualityIssueAggregator.AddBuildIssues(summary, new[]
            {
                new QualityIssue(
                    "build:SDK_PROBE_FAILED",
                    QualityIssueSeverity.Error,
                    QualityIssueSource.Build,
                    workingDir,
                    "SDK_PROBE_FAILED",
                    $"Failed to determine .NET SDK from '{workingDir}' (exit {result.ExitCode}). Install .NET SDK 9.0.200+ on PATH or pin a 9.x SDK in {Path.Combine(workingDir, "global.json")}.",
                    summary.Scorecard.BaseOverallScore,
                    "NO-GO: cannot resolve a .NET SDK for the target solution.")
            });
            return false;
        }

        var resolved = ParseVersion(result.StdOut);
        if (resolved is null)
        {
            QualityIssueAggregator.AddBuildIssues(summary, new[]
            {
                new QualityIssue(
                    "build:SDK_PROBE_PARSE",
                    QualityIssueSeverity.Error,
                    QualityIssueSource.Build,
                    workingDir,
                    "SDK_PROBE_PARSE",
                    $"Could not parse 'dotnet --version' output: '{result.StdOut.Trim()}'.",
                    summary.Scorecard.BaseOverallScore,
                    "NO-GO: cannot validate .NET SDK for the target solution.")
            });
            return false;
        }

        if (resolved < SlnxMinimumSdk)
        {
            QualityIssueAggregator.AddBuildIssues(summary, new[]
            {
                new QualityIssue(
                    "build:SLNX_REQUIRES_NET9",
                    QualityIssueSeverity.Error,
                    QualityIssueSource.Build,
                    solutionPath,
                    "SLNX_REQUIRES_NET9",
                    $"Solution '{Path.GetFileName(solutionPath)}' uses the .slnx XML format which requires .NET SDK {SlnxMinimumSdk}+. Resolved SDK in '{workingDir}' is {resolved}. Install .NET SDK {SlnxMinimumSdk}+ or pin a 9.x SDK in '{Path.Combine(workingDir, "global.json")}'.",
                    summary.Scorecard.BaseOverallScore,
                    $"NO-GO: .slnx solution requires .NET SDK {SlnxMinimumSdk}+; resolved {resolved}.")
            });
            return false;
        }

        return true;
    }

    private static Version? ParseVersion(string output)
    {
        var line = output
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(s => char.IsDigit(s.FirstOrDefault()));
        if (line is null) return null;
        var core = line.Split('-', '+')[0];
        return Version.TryParse(core, out var v) ? v : null;
    }

    private async Task CollectRuntimeReadinessAsync(RunSummary summary, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.DeploymentMetadataPath))
        {
            summary.RuntimeReadiness = RuntimeReadinessDecision.Unknown;
            summary.RuntimeReadinessRationale = "Deployment metadata was not configured.";
            return;
        }

        summary.DeploymentMetadata = _metadataValidator.LoadAndValidate(_settings.DeploymentMetadataPath);
        var probes = await _probeRunner.RunAsync(_settings, summary.DeploymentMetadata, cancellationToken).ConfigureAwait(false);
        summary.SyntheticProbes.AddRange(probes);

        if (!summary.DeploymentMetadata.IsValid)
        {
            summary.RuntimeReadiness = RuntimeReadinessDecision.NotReady;
            summary.RuntimeReadinessRationale = "Deployment metadata validation failed.";
            return;
        }

        if (string.Equals(_settings.ProbeMode, "Disabled", StringComparison.OrdinalIgnoreCase))
        {
            summary.RuntimeReadiness = RuntimeReadinessDecision.Unknown;
            summary.RuntimeReadinessRationale = "Synthetic probes are disabled.";
            return;
        }

        if (summary.SyntheticProbes.Any(p => p.Status is SyntheticProbeStatus.Failed or SyntheticProbeStatus.Error))
        {
            summary.RuntimeReadiness = RuntimeReadinessDecision.NotReady;
            summary.RuntimeReadinessRationale = "One or more synthetic probes failed.";
            return;
        }

        summary.RuntimeReadiness = RuntimeReadinessDecision.Ready;
        summary.RuntimeReadinessRationale = "Deployment metadata is valid and synthetic probes passed or were skipped.";
    }
}
