using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Data;
using Clipboard = System.Windows.Clipboard;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SI360.GateRunner.Models;
using SI360.GateRunner.Services;

namespace SI360.GateRunner.ViewModels;

public sealed partial class TestScenarioMatrixViewModel : ObservableObject
{
    private readonly RunnerSettings _settings;
    private readonly ITestScenarioMatrixLoader _loader;
    private readonly DotnetTestRunner _runner;
    private readonly TrxResultParser _parser;
    private TestScenarioMatrixDocument _document = new();
    private CancellationTokenSource? _runCts;

    public TestScenarioMatrixViewModel(
        RunnerSettings settings,
        ITestScenarioMatrixLoader loader,
        DotnetTestRunner runner,
        TrxResultParser parser)
    {
        _settings = settings;
        _loader = loader;
        _runner = runner;
        _parser = parser;
        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = FilterItem;
        ItemsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(TestScenarioMatrixItemViewModel.Area)));
        Refresh();
    }

    public event Action<string>? LogLine;

    public ObservableCollection<TestScenarioMatrixItemViewModel> Items { get; } = new();
    public ICollectionView ItemsView { get; }

    [ObservableProperty] private TestScenarioMatrixItemViewModel? selectedItem;
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private string statusText = "Test scenario matrix not loaded.";
    [ObservableProperty] private string runStatusText = "Ready to run mapped unit/integration/repository/service scenarios.";
    [ObservableProperty] private string sourcePathText = string.Empty;
    [ObservableProperty] private bool hasLoadErrors;
    [ObservableProperty] private string loadErrorText = string.Empty;
    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private bool showUnit = true;
    [ObservableProperty] private bool showService = true;
    [ObservableProperty] private bool showRepository = true;
    [ObservableProperty] private bool showIntegration = true;
    [ObservableProperty] private bool showStatic = true;
    [ObservableProperty] private bool showOther = true;
    [ObservableProperty] private bool showNotRun = true;
    [ObservableProperty] private bool showQueued = true;
    [ObservableProperty] private bool showRunning = true;
    [ObservableProperty] private bool showPassed = true;
    [ObservableProperty] private bool showFailed = true;
    [ObservableProperty] private bool showBlocked = true;
    [ObservableProperty] private bool showSkipped = true;
    [ObservableProperty] private bool showNotImplemented = true;

    public int TotalCount => Items.Count;
    public int MappedCount => Items.Count(item => item.HasMappedTest);
    public int NotImplementedCount => Items.Count(item => !item.HasMappedTest);
    public int PassedCount => Items.Count(item => item.Status == TestScenarioMatrixStatus.Passed);
    public int FailedCount => Items.Count(item => item.Status == TestScenarioMatrixStatus.Failed);
    public int RunningCount => Items.Count(item => item.Status == TestScenarioMatrixStatus.Running);
    public int QueuedCount => Items.Count(item => item.Status == TestScenarioMatrixStatus.Queued);
    public int UnitCount => Items.Count(item => item.IsUnit);
    public int ServiceCount => Items.Count(item => item.IsService);
    public int RepositoryCount => Items.Count(item => item.IsRepository);
    public int IntegrationCount => Items.Count(item => item.IsIntegration);
    public int StaticCount => Items.Count(item => item.IsStatic);
    public string SummaryText => $"{TotalCount} scenarios | {MappedCount} mapped | {NotImplementedCount} not implemented | {PassedCount} passed | {FailedCount} failed";

    partial void OnSelectedItemChanged(TestScenarioMatrixItemViewModel? value) => NotifyRunCommands();
    partial void OnIsRunningChanged(bool value) => NotifyRunCommands();
    partial void OnSearchTextChanged(string value) => ItemsView.Refresh();
    partial void OnShowUnitChanged(bool value) => ItemsView.Refresh();
    partial void OnShowServiceChanged(bool value) => ItemsView.Refresh();
    partial void OnShowRepositoryChanged(bool value) => ItemsView.Refresh();
    partial void OnShowIntegrationChanged(bool value) => ItemsView.Refresh();
    partial void OnShowStaticChanged(bool value) => ItemsView.Refresh();
    partial void OnShowOtherChanged(bool value) => ItemsView.Refresh();
    partial void OnShowNotRunChanged(bool value) => ItemsView.Refresh();
    partial void OnShowQueuedChanged(bool value) => ItemsView.Refresh();
    partial void OnShowRunningChanged(bool value) => ItemsView.Refresh();
    partial void OnShowPassedChanged(bool value) => ItemsView.Refresh();
    partial void OnShowFailedChanged(bool value) => ItemsView.Refresh();
    partial void OnShowBlockedChanged(bool value) => ItemsView.Refresh();
    partial void OnShowSkippedChanged(bool value) => ItemsView.Refresh();
    partial void OnShowNotImplementedChanged(bool value) => ItemsView.Refresh();

    [RelayCommand]
    public void Refresh()
    {
        StatusText = "Loading test scenario matrix...";
        Items.Clear();

        _document = _loader.Load(_settings);
        SourcePathText = _document.SourcePath;
        HasLoadErrors = _document.LoadErrors.Count > 0;
        LoadErrorText = string.Join(Environment.NewLine, _document.LoadErrors);

        foreach (var item in _document.Items.Select(i => new TestScenarioMatrixItemViewModel(i)))
            Items.Add(item);

        SelectedItem = Items.FirstOrDefault();
        StatusText = HasLoadErrors
            ? "Test scenario matrix loaded with errors."
            : Items.Count == 0
                ? "No test scenario matrix rows found."
                : $"Test scenario matrix loaded. {SummaryText}";
        if (!IsRunning)
            RunStatusText = StatusText;

        RefreshCounts();
        ItemsView.Refresh();
        NotifyRunCommands();
    }

    [RelayCommand(CanExecute = nameof(CanRunScenarios))]
    private Task RunAllAsync() => RunScenariosAsync(Items.ToList());

    [RelayCommand(CanExecute = nameof(CanRunScenarios))]
    private Task RunFilteredAsync() => RunScenariosAsync(ItemsView.Cast<TestScenarioMatrixItemViewModel>().ToList());

    [RelayCommand(CanExecute = nameof(CanRunSelectedScenario))]
    private Task RunSelectedAsync()
    {
        if (SelectedItem is null) return Task.CompletedTask;
        return RunScenariosAsync(new[] { SelectedItem }.ToList());
    }

    [RelayCommand(CanExecute = nameof(CanCancelRun))]
    private void CancelRun()
    {
        _runCts?.Cancel();
        RunStatusText = "Cancelling matrix run...";
    }

    [RelayCommand]
    private void CopySelectedMarkdown()
    {
        if (SelectedItem is null) return;
        Clipboard.SetText(SelectedItem.Markdown);
    }

    [RelayCommand]
    private void OpenSourceDocument() => OpenPath(SourcePathText);

    private async Task RunScenariosAsync(IReadOnlyList<TestScenarioMatrixItemViewModel> requestedItems)
    {
        var candidates = requestedItems
            .Where(item => !item.IsQueuedOrRunning)
            .DistinctBy(item => item.Scenario)
            .ToList();

        if (candidates.Count == 0)
        {
            RunStatusText = "No eligible scenarios to run.";
            return;
        }

        IsRunning = true;
        _runCts = new CancellationTokenSource();
        var startedAt = DateTime.UtcNow;
        var runDir = Path.Combine(_settings.ResultsDirectory, $"TestScenarioMatrixRun_{startedAt:yyyyMMdd_HHmmss}Z");
        Directory.CreateDirectory(runDir);

        foreach (var item in candidates)
        {
            item.FailureDetails = string.Empty;
            item.Output = string.Empty;
            item.TrxPath = string.Empty;
            item.Status = item.HasMappedTest
                ? TestScenarioMatrixStatus.Queued
                : TestScenarioMatrixStatus.NotImplemented;
        }

        RefreshCounts();
        ItemsView.Refresh();

        try
        {
            var runnable = candidates.Where(item => item.HasMappedTest).ToList();
            if (runnable.Count == 0)
            {
                RunStatusText = "No mapped automated tests exist for the selected matrix scenarios.";
                LogLine?.Invoke("[MATRIX] No mapped automated tests exist for the selected matrix scenarios.");
                return;
            }

            var completed = 0;
            foreach (var item in runnable)
            {
                _runCts.Token.ThrowIfCancellationRequested();
                item.Status = TestScenarioMatrixStatus.Running;
                item.FailureDetails = string.Empty;
                RunStatusText = $"Running matrix scenario {completed + 1}/{runnable.Count}: {item.Scenario}";
                RefreshCounts();

                var filter = $"FullyQualifiedName~{item.MappedTestFilter}";
                LogLine?.Invoke($"[MATRIX] Running {item.Scenario}: {filter}");
                var (exit, trxPath, stdOut) = await _runner.RunGateAsync(
                    $"matrix-{Slug(item.Scenario)}",
                    filter,
                    runDir,
                    new Progress<string>(line => LogLine?.Invoke(line)),
                    _runCts.Token).ConfigureAwait(true);

                item.TrxPath = trxPath;
                item.Output = stdOut;
                var outcomes = _parser.Parse(trxPath, item.Scenario);
                var outcome = outcomes.FirstOrDefault(test =>
                    test.TestName.Contains(item.Scenario, StringComparison.OrdinalIgnoreCase) ||
                    test.TestName.Contains(Path.GetFileName(item.MappedTestFilter), StringComparison.OrdinalIgnoreCase));

                if (outcome is null)
                {
                    item.Status = exit == 0 ? TestScenarioMatrixStatus.Passed : TestScenarioMatrixStatus.Blocked;
                    item.FailureDetails = exit == 0
                        ? string.Empty
                        : $"dotnet test exit {exit}; no matching TRX result parsed. TRX: {trxPath}";
                }
                else
                {
                    ApplyOutcome(item, outcome);
                }

                if (item.Status == TestScenarioMatrixStatus.Failed ||
                    item.Status == TestScenarioMatrixStatus.Blocked)
                {
                    LogLine?.Invoke($"[MATRIX] {item.Scenario} {item.StatusLabel}: {item.FailureDetails}");
                }

                completed++;
                RefreshCounts();
                ItemsView.Refresh();
            }

            RunStatusText = $"Matrix run complete. {PassedCount} passed, {FailedCount} failed, {NotImplementedCount} not implemented.";
        }
        catch (OperationCanceledException)
        {
            foreach (var item in candidates.Where(item => item.IsQueuedOrRunning))
                item.Status = TestScenarioMatrixStatus.Skipped;
            RunStatusText = "Matrix run cancelled.";
        }
        catch (Exception ex)
        {
            foreach (var item in candidates.Where(item => item.IsQueuedOrRunning))
            {
                item.Status = TestScenarioMatrixStatus.Blocked;
                item.FailureDetails = ex.Message;
            }
            RunStatusText = $"Matrix run failed: {ex.Message}";
            LogLine?.Invoke($"[MATRIX] ERROR: {ex}");
        }
        finally
        {
            _runCts?.Dispose();
            _runCts = null;
            IsRunning = false;
            RefreshCounts();
            ItemsView.Refresh();
        }
    }

    private bool FilterItem(object obj)
    {
        if (obj is not TestScenarioMatrixItemViewModel item) return false;

        var typeVisible =
            (ShowUnit && item.IsUnit) ||
            (ShowService && item.IsService) ||
            (ShowRepository && item.IsRepository) ||
            (ShowIntegration && item.IsIntegration) ||
            (ShowStatic && item.IsStatic) ||
            (ShowOther && item.IsOther);
        if (!typeVisible) return false;

        if (!IsStatusVisible(item.Status)) return false;

        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        var haystack = string.Join('\n',
            item.Area,
            item.Scenario,
            item.RecommendedTestType,
            item.SetupInputs,
            item.ExpectedResult,
            item.SuggestedTargetCode,
            item.Notes,
            item.MappedTestFilter,
            item.StatusLabel,
            item.FailureDetails,
            item.TrxPath);
        return haystack.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsStatusVisible(TestScenarioMatrixStatus status) => status switch
    {
        TestScenarioMatrixStatus.NotRun => ShowNotRun,
        TestScenarioMatrixStatus.Queued => ShowQueued,
        TestScenarioMatrixStatus.Running => ShowRunning,
        TestScenarioMatrixStatus.Passed => ShowPassed,
        TestScenarioMatrixStatus.Failed => ShowFailed,
        TestScenarioMatrixStatus.Blocked => ShowBlocked,
        TestScenarioMatrixStatus.Skipped => ShowSkipped,
        TestScenarioMatrixStatus.NotImplemented => ShowNotImplemented,
        _ => true
    };

    private static void OpenPath(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            var target = File.Exists(path) ? path : Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(target)) return;
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch
        {
            // Source opening is best-effort; the path remains visible for manual inspection.
        }
    }

    private void RefreshCounts()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(MappedCount));
        OnPropertyChanged(nameof(NotImplementedCount));
        OnPropertyChanged(nameof(PassedCount));
        OnPropertyChanged(nameof(FailedCount));
        OnPropertyChanged(nameof(RunningCount));
        OnPropertyChanged(nameof(QueuedCount));
        OnPropertyChanged(nameof(UnitCount));
        OnPropertyChanged(nameof(ServiceCount));
        OnPropertyChanged(nameof(RepositoryCount));
        OnPropertyChanged(nameof(IntegrationCount));
        OnPropertyChanged(nameof(StaticCount));
        OnPropertyChanged(nameof(SummaryText));
    }

    private bool CanRunScenarios() => !IsRunning && Items.Count > 0;
    private bool CanRunSelectedScenario() => !IsRunning && SelectedItem is not null;
    private bool CanCancelRun() => IsRunning;

    private void NotifyRunCommands()
    {
        RunAllCommand.NotifyCanExecuteChanged();
        RunFilteredCommand.NotifyCanExecuteChanged();
        RunSelectedCommand.NotifyCanExecuteChanged();
        CancelRunCommand.NotifyCanExecuteChanged();
    }

    private static void ApplyOutcome(TestScenarioMatrixItemViewModel item, TestOutcome outcome)
    {
        item.Status = outcome.Status switch
        {
            TestStatus.Passed => TestScenarioMatrixStatus.Passed,
            TestStatus.Failed => TestScenarioMatrixStatus.Failed,
            TestStatus.Skipped => TestScenarioMatrixStatus.Skipped,
            _ => TestScenarioMatrixStatus.Blocked
        };
        item.FailureDetails = outcome.Status == TestStatus.Failed
            ? string.Join(Environment.NewLine,
                new[] { outcome.ErrorMessage, outcome.StackTrace }.Where(text => !string.IsNullOrWhiteSpace(text)))
            : string.Empty;
    }

    private static string Slug(string value)
    {
        var chars = value.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray();
        return string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }
}
