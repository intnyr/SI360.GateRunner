using System.Text.Json;
using SI360.GateRunner.Models;

namespace SI360.GateRunner.Services;

public interface IFlaUiCoverageService
{
    FlaUiCoverageRun Load(RunnerSettings settings, string? manifestPath = null);
}

public sealed class FlaUiCoverageService : IFlaUiCoverageService
{
    private readonly IFlaUiCoverageManifestLoader _manifestLoader;
    private readonly TrxResultParser _trxParser;

    public FlaUiCoverageService(IFlaUiCoverageManifestLoader manifestLoader, TrxResultParser trxParser)
    {
        _manifestLoader = manifestLoader;
        _trxParser = trxParser;
    }

    public FlaUiCoverageRun Load(RunnerSettings settings, string? manifestPath = null)
    {
        var path = string.IsNullOrWhiteSpace(manifestPath)
            ? _manifestLoader.ResolveDefaultManifestPath()
            : manifestPath;
        var run = new FlaUiCoverageRun { ManifestPath = path };
        run.RuntimeConfiguration = BuildRuntimeConfiguration(settings);
        run.LoadWarnings.AddRange(run.RuntimeConfiguration.Warnings);
        var manifestResult = _manifestLoader.Load(path, settings);
        run.LoadErrors.AddRange(manifestResult.Errors);
        run.LoadWarnings.AddRange(manifestResult.Warnings);

        if (manifestResult.Manifest is null)
        {
            run.Summary = BuildSummary(run.Sections);
            return run;
        }

        var outcomes = LoadLatestOutcomes(settings);
        foreach (var section in manifestResult.Manifest.Sections)
        {
            var sectionResult = new FlaUiCoverageSectionResult
            {
                Group = section.Group,
                Name = section.Name,
                SourceDocument = section.SourceDocument
            };

            foreach (var item in section.Items)
                sectionResult.Items.Add(Correlate(item, outcomes));

            run.Sections.Add(sectionResult);
        }

        run.Summary = BuildSummary(run.Sections);
        return run;
    }

    private IReadOnlyList<(TestOutcome Outcome, string TrxPath)> LoadLatestOutcomes(RunnerSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ResultsDirectory) || !Directory.Exists(settings.ResultsDirectory))
            return Array.Empty<(TestOutcome, string)>();

        var files = Directory.EnumerateFiles(settings.ResultsDirectory, "*.trx", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(50)
            .ToList();

        var results = new List<(TestOutcome Outcome, string TrxPath)>();
        foreach (var file in files)
        {
            foreach (var outcome in _trxParser.Parse(file.FullName, "FlaUI"))
                results.Add((outcome, file.FullName));
        }

        return results;
    }

    private static FlaUiCoverageItemResult Correlate(
        FlaUiCoverageItem item,
        IReadOnlyList<(TestOutcome Outcome, string TrxPath)> outcomes)
    {
        var result = new FlaUiCoverageItemResult { Item = item };
        result.EvidencePaths.AddRange(item.EvidencePaths.Where(p => !string.IsNullOrWhiteSpace(p)));
        result.EvidencePaths.AddRange(item.SourceFiles.Where(p => !string.IsNullOrWhiteSpace(p)));

        var matched = outcomes
            .Where(o => MatchesAnyFilter(o.Outcome.TestName, item.TestFilters))
            .OrderByDescending(o => File.Exists(o.TrxPath) ? File.GetLastWriteTimeUtc(o.TrxPath) : DateTime.MinValue)
            .ToList();

        if (matched.Count == 0)
        {
            result.ExecutionStatus = FlaUiExecutionStatus.NotRun;
            result.Status = item.AutomationStatus switch
            {
                FlaUiAutomationStatus.NotAutomated => FlaUiCoverageStatus.NotStarted,
                FlaUiAutomationStatus.NeedsReview => FlaUiCoverageStatus.NeedsReview,
                _ => FlaUiCoverageStatus.NeedsReview
            };
            return result;
        }

        var latest = matched[0];
        result.LatestTestName = latest.Outcome.TestName;
        result.TrxPath = latest.TrxPath;
        result.ErrorMessage = latest.Outcome.ErrorMessage;
        if (!string.IsNullOrWhiteSpace(latest.TrxPath))
            result.EvidencePaths.Add(latest.TrxPath);

        var blockerReason = matched.Select(GetBlockerReason).FirstOrDefault(reason => !string.IsNullOrWhiteSpace(reason));
        if (!string.IsNullOrWhiteSpace(blockerReason))
        {
            result.ExecutionStatus = FlaUiExecutionStatus.Blocked;
            result.Status = FlaUiCoverageStatus.Blocked;
            result.BlockerReason = blockerReason;
            return result;
        }

        if (matched.Any(m => m.Outcome.Status == TestStatus.Failed))
        {
            var failed = matched.First(m => m.Outcome.Status == TestStatus.Failed);
            result.ExecutionStatus = FlaUiExecutionStatus.Failed;
            result.Status = FlaUiCoverageStatus.Failed;
            result.ErrorMessage = failed.Outcome.ErrorMessage;
            return result;
        }

        if (matched.Any(m => m.Outcome.Status == TestStatus.Passed))
        {
            result.ExecutionStatus = FlaUiExecutionStatus.Passed;
            result.Status = item.AutomationStatus == FlaUiAutomationStatus.Automated
                ? FlaUiCoverageStatus.Passed
                : FlaUiCoverageStatus.NeedsReview;
            return result;
        }

        result.ExecutionStatus = FlaUiExecutionStatus.Unknown;
        result.Status = FlaUiCoverageStatus.NeedsReview;
        return result;
    }

    private static bool MatchesAnyFilter(string testName, IReadOnlyCollection<string> filters)
    {
        if (filters.Count == 0) return false;
        return filters.Any(filter =>
        {
            var term = filter;
            var idx = term.IndexOf('~');
            if (idx >= 0 && idx + 1 < term.Length)
                term = term[(idx + 1)..];
            return testName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                   term.Contains(testName, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static string? GetBlockerReason((TestOutcome Outcome, string TrxPath) match)
    {
        var text = string.Join('\n', match.Outcome.ErrorMessage, match.Outcome.StackTrace, match.Outcome.StdOut);
        if (text.Contains("SI360_UI_VALID_PIN is required", StringComparison.OrdinalIgnoreCase))
            return "SI360_UI_VALID_PIN is required for Auth=Required FlaUI tests.";
        if (text.Contains("SI360_UI_APP_PATH is required", StringComparison.OrdinalIgnoreCase))
            return "SI360_UI_APP_PATH is required for FlaUI tests.";
        if (text.Contains("PIN login page was not ready", StringComparison.OrdinalIgnoreCase))
            return "SI360 launched, but the PIN login page was not ready for FlaUI automation.";
        if (text.Contains("Unable to acquire current main window", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("AppFixture is not ready", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("AppFixture", StringComparison.OrdinalIgnoreCase) &&
            text.Contains("WindowTracker", StringComparison.OrdinalIgnoreCase))
            return "FlaUI fixture could not acquire the SI360 main window.";
        return null;
    }

    private static FlaUiCoverageSummary BuildSummary(IEnumerable<FlaUiCoverageSectionResult> sections)
    {
        var items = sections.SelectMany(s => s.Items).ToList();
        return new FlaUiCoverageSummary
        {
            Total = items.Count,
            CanonicalTotal = items.Count(i => !i.Item.IsDerived),
            DerivedTotal = items.Count(i => i.Item.IsDerived),
            Automated = items.Count(i => i.Item.AutomationStatus is FlaUiAutomationStatus.Automated or FlaUiAutomationStatus.PartiallyAutomated),
            Passed = items.Count(i => i.Status == FlaUiCoverageStatus.Passed),
            Failed = items.Count(i => i.Status == FlaUiCoverageStatus.Failed),
            Blocked = items.Count(i => i.Status == FlaUiCoverageStatus.Blocked),
            NeedsReview = items.Count(i => i.Status == FlaUiCoverageStatus.NeedsReview),
            NotStarted = items.Count(i => i.Status == FlaUiCoverageStatus.NotStarted)
        };
    }

    private static FlaUiRuntimeConfiguration BuildRuntimeConfiguration(RunnerSettings settings)
    {
        var configuration = new FlaUiRuntimeConfiguration
        {
            SolutionPath = settings.SolutionPath,
            TestProjectPath = settings.TestProjectPath,
            FlaUiTestProjectPath = settings.ResolveFlaUiTestProjectPath(),
            Si360UiAppPath = settings.ResolveSi360UiAppPath(),
            ResultsDirectory = settings.ResultsDirectory,
            PinSource = ResolvePinSource(settings)
        };

        var solutionDirectory = string.IsNullOrWhiteSpace(settings.SolutionPath)
            ? string.Empty
            : Path.GetDirectoryName(settings.SolutionPath) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(solutionDirectory))
            return configuration;

        var testingConfigurationPath = Path.Combine(solutionDirectory, "SI360.UI", "appsettings.Testing.json");
        configuration.TestingConfigurationPath = testingConfigurationPath;
        if (!File.Exists(testingConfigurationPath))
        {
            configuration.Warnings.Add($"SI360 testing configuration was not found: {testingConfigurationPath}");
            return configuration;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(testingConfigurationPath));
            AddFlag(doc, configuration, "SignalR.Enabled");
            AddFlag(doc, configuration, "Development.EnablePublicButton");
            AddFlag(doc, configuration, "Development.DatacapCreditCard");
            AddFlag(doc, configuration, "Development.EnableCashierDrawer");
            AddFlag(doc, configuration, "Development.Loyalty");
            AddFlag(doc, configuration, "Development.EnableQSR");
            AddFlag(doc, configuration, "Development.EnableQA");
            AddFlag(doc, configuration, "Logging.BaseDirectory");
        }
        catch (Exception ex)
        {
            configuration.Warnings.Add($"SI360 testing configuration could not be parsed: {ex.Message}");
            return configuration;
        }

        WarnWhenFalse(configuration, "SignalR.Enabled", "SignalR is disabled in appsettings.Testing.json; send-order/KDS workflows may be limited to UI-state verification.");
        WarnWhenFalse(configuration, "Development.EnablePublicButton", "Public Customers may be blocked because Development.EnablePublicButton is disabled in appsettings.Testing.json.");
        WarnWhenFalse(configuration, "Development.DatacapCreditCard", "Credit-card and gift-card processor flows may be blocked because Development.DatacapCreditCard is disabled in appsettings.Testing.json.");
        WarnWhenFalse(configuration, "Development.EnableCashierDrawer", "Cash drawer hardware flows are disabled in appsettings.Testing.json.");
        WarnWhenFalse(configuration, "Development.Loyalty", "Loyalty-dependent flows are disabled in appsettings.Testing.json.");
        WarnWhenFalse(configuration, "Development.EnableQSR", "QSR-dependent flows are disabled in appsettings.Testing.json.");
        return configuration;
    }

    private static string ResolvePinSource(RunnerSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Si360UiValidPin))
            return "RunnerSettings.Si360UiValidPin";
        return string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SI360_UI_VALID_PIN"))
            ? "Missing"
            : "SI360_UI_VALID_PIN";
    }

    private static void AddFlag(JsonDocument doc, FlaUiRuntimeConfiguration configuration, string path)
    {
        if (!TryGetProperty(doc.RootElement, path.Split('.'), out var value))
            return;

        configuration.RuntimeFlags[path] = value.ValueKind switch
        {
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.String => value.GetString() ?? string.Empty,
            _ => value.ToString()
        };
    }

    private static bool TryGetProperty(JsonElement root, IReadOnlyList<string> path, out JsonElement value)
    {
        value = root;
        foreach (var segment in path)
        {
            if (value.ValueKind != JsonValueKind.Object ||
                !value.TryGetProperty(segment, out value))
            {
                return false;
            }
        }

        return true;
    }

    private static void WarnWhenFalse(FlaUiRuntimeConfiguration configuration, string key, string warning)
    {
        if (configuration.RuntimeFlags.TryGetValue(key, out var value) &&
            string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
        {
            configuration.Warnings.Add(warning);
        }
    }
}
