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

    public GateRunOrchestrator(
        RunnerSettings settings,
        DotnetTestRunner testRunner,
        BuildErrorCollector buildErrorCollector,
        TrxResultParser parser,
        ScorecardAggregator aggregator,
        IDecisionPolicy decisionPolicy,
        IGateDiscoveryService gateDiscovery,
        IReportWriter reportWriter,
        IProcessRunner processRunner)
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
    }

    public async Task<RunSummary> RunAsync(
        GateRunRequest request,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTime.Now;
        var runDir = Path.Combine(_settings.ResultsDirectory, $"GateRun_{startedAt:yyyyMMdd_HHmmss}");
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

        if (request.BuildFirst)
        {
            log?.Report("Restoring...");
            var restore = await _testRunner.RestoreAsync(log, cancellationToken, runDir).ConfigureAwait(false);
            if (restore.ExitCode != 0)
            {
                summary.BuildErrors.Add(new BuildError(_settings.TestProjectPath, 0, 0, "RESTORE", "dotnet restore failed."));
                await FinalizeAsync(summary, startedAt).ConfigureAwait(false);
                return summary;
            }

            log?.Report("Building...");
            var (buildExit, errors) = await _buildErrorCollector.BuildAsync(log, cancellationToken, runDir).ConfigureAwait(false);
            summary.BuildErrors.AddRange(errors);
            if (buildExit != 0 || errors.Count > 0)
            {
                await FinalizeAsync(summary, startedAt).ConfigureAwait(false);
                return summary;
            }
        }

        foreach (var gate in GateCatalog.All)
        {
            cancellationToken.ThrowIfCancellationRequested();
            log?.Report($"Running {gate.DisplayName}...");
            var started = DateTime.Now;
            var (exit, trxPath, stdOut) = await _testRunner
                .RunGateAsync(gate.Id, gate.TestClassFilter, runDir, log, cancellationToken)
                .ConfigureAwait(false);

            var outcomes = _parser.Parse(trxPath, gate.Id);
            var result = new GateResult
            {
                Definition = gate,
                Duration = DateTime.Now - started,
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
        var decision = _decisionPolicy.Decide(summary);
        summary.Decision = decision.Decision;
        summary.DecisionPolicyName = decision.PolicyName;
        summary.DecisionPolicyVersion = decision.PolicyVersion;
        summary.DecisionRationale = decision.Rationale;
        summary.Duration = DateTime.Now - startedAt;
        var (md, json) = await _reportWriter.WriteAsync(summary, _settings.ResultsDirectory).ConfigureAwait(false);
        summary.ReportMarkdownPath = md;
        summary.ReportJsonPath = json;
    }
}
