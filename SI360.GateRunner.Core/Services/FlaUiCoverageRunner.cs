using System.Text;
using SI360.GateRunner.Models;

namespace SI360.GateRunner.Services;

public sealed class FlaUiCoverageRunRequest
{
    public FlaUiCoverageGroup? Group { get; set; }
    public FlaUiCoverageStatus? Status { get; set; }
    public string? Scenario { get; set; }
    public IReadOnlyCollection<string> ItemIds { get; set; } = Array.Empty<string>();
}

public sealed class FlaUiCoverageSkippedItem
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Reason { get; init; }
}

public sealed class FlaUiCoverageExecutionResult
{
    public int ExitCode { get; set; }
    public int MatchedCount { get; set; }
    public int RunnableCount { get; set; }
    public string? RunDirectory { get; set; }
    public string? TrxPath { get; set; }
    public string? Filter { get; set; }
    public string StdOut { get; set; } = string.Empty;
    public string StdErr { get; set; } = string.Empty;
    public bool TimedOut { get; set; }
    public bool Canceled { get; set; }
    public List<string> Errors { get; } = new();
    public List<FlaUiCoverageSkippedItem> SkippedItems { get; } = new();

    public bool StartedProcess => !string.IsNullOrWhiteSpace(RunDirectory);
    public bool Succeeded => StartedProcess && ExitCode == 0 && !TimedOut && !Canceled && Errors.Count == 0;
}

public interface IFlaUiCoverageRunner
{
    Task<FlaUiCoverageExecutionResult> RunAsync(
        FlaUiCoverageRunRequest request,
        IProgress<string>? log,
        CancellationToken cancellationToken);
}

public sealed class FlaUiCoverageRunner : IFlaUiCoverageRunner
{
    private readonly RunnerSettings _settings;
    private readonly IFlaUiCoverageService _coverageService;
    private readonly IProcessRunner _processRunner;

    public FlaUiCoverageRunner(
        RunnerSettings settings,
        IFlaUiCoverageService coverageService,
        IProcessRunner processRunner)
    {
        _settings = settings;
        _coverageService = coverageService;
        _processRunner = processRunner;
    }

    public async Task<FlaUiCoverageExecutionResult> RunAsync(
        FlaUiCoverageRunRequest request,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        var result = new FlaUiCoverageExecutionResult();
        var coverage = _coverageService.Load(_settings);
        result.Errors.AddRange(coverage.LoadErrors);
        if (result.Errors.Count > 0)
            return result;

        var testProjectPath = _settings.ResolveFlaUiTestProjectPath();
        if (string.IsNullOrWhiteSpace(testProjectPath) || !File.Exists(testProjectPath))
        {
            result.Errors.Add($"FlaUI test project was not found. Configure FlaUiTestProjectPath or add SI360.UITests/SI360.UITests.csproj next to the solution. Resolved path: '{testProjectPath}'.");
            return result;
        }

        var appPath = _settings.ResolveSi360UiAppPath();
        if (string.IsNullOrWhiteSpace(appPath) || !File.Exists(appPath))
        {
            result.Errors.Add($"SI360 UI app executable was not found. Configure Si360UiAppPath or build SI360.UI. Resolved path: '{appPath}'.");
            return result;
        }

        var candidates = SelectCandidates(coverage, request).ToList();
        result.MatchedCount = candidates.Count;
        if (candidates.Count == 0)
        {
            result.Errors.Add("No FlaUI coverage scenarios matched the requested selection.");
            return result;
        }

        var filters = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in candidates)
        {
            var runnableFilters = item.Item.TestFilters
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Select(NormalizeFilter)
                .ToList();

            if (item.Item.AutomationStatus == FlaUiAutomationStatus.NotAutomated)
            {
                result.SkippedItems.Add(Skipped(item, "Scenario is marked NotAutomated."));
                continue;
            }

            if (runnableFilters.Count == 0)
            {
                result.SkippedItems.Add(Skipped(item, "Scenario has no runnable test filters."));
                continue;
            }

            foreach (var filter in runnableFilters)
                filters.Add(filter);
        }

        result.RunnableCount = filters.Count;
        if (filters.Count == 0)
        {
            result.Errors.Add("No runnable FlaUI test filters were available for the requested selection.");
            return result;
        }

        var startedAt = DateTime.UtcNow;
        var runDir = Path.Combine(_settings.ResultsDirectory, $"FlaUiCoverageRun_{startedAt:yyyyMMdd_HHmmss}Z");
        Directory.CreateDirectory(runDir);
        var trxName = $"FlaUiCoverage_{startedAt:yyyyMMdd_HHmmss}Z.trx";
        var filterText = filters.Count == 1 ? filters.Single() : $"({string.Join("|", filters)})";
        result.RunDirectory = runDir;
        result.TrxPath = Path.Combine(runDir, trxName);
        result.Filter = filterText;

        log?.Report($"FlaUI test project: {testProjectPath}");
        log?.Report($"SI360 UI app path: {appPath}");
        log?.Report($"Running FlaUI coverage: {candidates.Count} scenarios, {filters.Count} unique test filters.");
        if (result.SkippedItems.Count > 0)
            log?.Report($"Skipping {result.SkippedItems.Count} non-runnable scenario(s).");

        var output = new StringBuilder();
        var capture = new Progress<string>(line =>
        {
            output.AppendLine(line);
            log?.Report(line);
        });

        var process = await _processRunner.RunAsync(
            GateRunnerCommands.FlaUiCoverage(_settings, filterText, runDir, trxName, filters.Count),
            capture,
            cancellationToken).ConfigureAwait(false);

        result.ExitCode = process.ExitCode;
        result.StdOut = output.Length == 0 ? process.StdOut : output.ToString();
        result.StdErr = process.StdErr;
        result.TimedOut = process.TimedOut;
        result.Canceled = process.Canceled;
        if (process.TimedOut)
            result.Errors.Add($"FlaUI coverage run exceeded the timeout of {process.ArtifactDirectory ?? runDir}.");
        if (process.Canceled)
            result.Errors.Add("FlaUI coverage run was canceled.");
        if ((process.TimedOut || process.Canceled || process.ExitCode != 0) && !File.Exists(result.TrxPath))
            result.Errors.Add($"FlaUI coverage run did not produce expected TRX: {result.TrxPath}.");
        if (process.TimedOut || process.Canceled || process.ExitCode != 0 || !string.IsNullOrWhiteSpace(process.Diagnostics))
            WriteFlaUiDiagnostics(result, process, testProjectPath, appPath);
        return result;
    }

    private static IEnumerable<FlaUiCoverageItemResult> SelectCandidates(
        FlaUiCoverageRun coverage,
        FlaUiCoverageRunRequest request)
    {
        var ids = new HashSet<string>(request.ItemIds.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);
        foreach (var item in coverage.Sections.SelectMany(s => s.Items))
        {
            if (ids.Count > 0 && !ids.Contains(item.Item.Id)) continue;
            if (request.Group is not null && item.Item.Group != request.Group.Value) continue;
            if (request.Status is not null && item.Status != request.Status.Value) continue;
            if (!MatchesScenario(item.Item, request.Scenario)) continue;
            yield return item;
        }
    }

    private static bool MatchesScenario(FlaUiCoverageItem item, string? scenario)
    {
        if (string.IsNullOrWhiteSpace(scenario)) return true;
        var normalizedScenario = NormalizeScenario(scenario);
        return item.Id.Equals(scenario, StringComparison.OrdinalIgnoreCase) ||
               item.Name.Contains(scenario, StringComparison.OrdinalIgnoreCase) ||
               NormalizeScenario(item.Id).Contains(normalizedScenario, StringComparison.OrdinalIgnoreCase) ||
               NormalizeScenario(item.Name).Contains(normalizedScenario, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeScenario(string value)
    {
        var chars = value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray();
        return new string(chars);
    }

    private static FlaUiCoverageSkippedItem Skipped(FlaUiCoverageItemResult item, string reason) => new()
    {
        Id = item.Item.Id,
        Name = item.Item.Name,
        Reason = reason
    };

    private static string NormalizeFilter(string filter)
    {
        var clean = filter.Trim().Replace("\"", string.Empty);
        if (clean.Contains("=", StringComparison.Ordinal) ||
            clean.Contains("~", StringComparison.Ordinal) ||
            clean.Contains("!=", StringComparison.Ordinal))
        {
            return clean;
        }

        return $"FullyQualifiedName~{clean}";
    }

    private static void WriteFlaUiDiagnostics(
        FlaUiCoverageExecutionResult result,
        ProcessRunResult process,
        string testProjectPath,
        string appPath)
    {
        if (string.IsNullOrWhiteSpace(result.RunDirectory))
            return;

        Directory.CreateDirectory(result.RunDirectory);
        var sb = new StringBuilder();
        sb.AppendLine("FlaUI coverage diagnostics");
        sb.AppendLine($"UTC: {DateTime.UtcNow:O}");
        sb.AppendLine($"ExitCode: {process.ExitCode}");
        sb.AppendLine($"TimedOut: {process.TimedOut}");
        sb.AppendLine($"Canceled: {process.Canceled}");
        sb.AppendLine($"Filter: {result.Filter}");
        sb.AppendLine($"FlaUI test project: {testProjectPath}");
        sb.AppendLine($"SI360 UI app path: {appPath}");
        sb.AppendLine($"Expected TRX: {result.TrxPath}");
        sb.AppendLine($"Expected TRX exists: {(!string.IsNullOrWhiteSpace(result.TrxPath) && File.Exists(result.TrxPath))}");
        sb.AppendLine();
        sb.AppendLine("Errors:");
        foreach (var error in result.Errors)
            sb.AppendLine($"- {error}");
        sb.AppendLine();
        sb.AppendLine("Run directory files:");
        foreach (var file in EnumerateRunDirectoryFiles(result.RunDirectory))
            sb.AppendLine($"- {file}");

        if (!string.IsNullOrWhiteSpace(process.Diagnostics))
        {
            sb.AppendLine();
            sb.AppendLine("Process diagnostics:");
            sb.AppendLine(process.Diagnostics.TrimEnd());
        }

        File.WriteAllText(Path.Combine(result.RunDirectory, "flaui-coverage.diagnostics.txt"), sb.ToString());
    }

    private static IEnumerable<string> EnumerateRunDirectoryFiles(string runDirectory)
    {
        if (!Directory.Exists(runDirectory))
            yield break;

        foreach (var file in Directory.EnumerateFiles(runDirectory, "*", SearchOption.TopDirectoryOnly)
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            yield return Path.GetFileName(file) ?? file;
        }
    }
}
