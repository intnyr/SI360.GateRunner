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

public sealed partial class FlaUiScenarioMatrixViewModel : ObservableObject
{
    private readonly RunnerSettings _settings;
    private readonly IFlaUiScenarioMatrixLoader _loader;
    private readonly DotnetTestRunner _runner;
    private readonly TrxResultParser _parser;
    private FlaUiScenarioMatrixDocument _document = new();
    private CancellationTokenSource? _runCts;

    public FlaUiScenarioMatrixViewModel(
        RunnerSettings settings,
        IFlaUiScenarioMatrixLoader loader,
        DotnetTestRunner runner,
        TrxResultParser parser)
    {
        _settings = settings;
        _loader = loader;
        _runner = runner;
        _parser = parser;
        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = FilterItem;
        ItemsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(FlaUiScenarioMatrixItemViewModel.Priority)));
        ItemsView.SortDescriptions.Add(new SortDescription(nameof(FlaUiScenarioMatrixItemViewModel.SourceOrder), ListSortDirection.Ascending));
        Refresh();
    }

    public event Action<string>? LogLine;

    public ObservableCollection<FlaUiScenarioMatrixItemViewModel> Items { get; } = new();
    public ICollectionView ItemsView { get; }

    [ObservableProperty] private FlaUiScenarioMatrixItemViewModel? selectedItem;
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private string statusText = "FlaUI scenario matrix not loaded.";
    [ObservableProperty] private string runStatusText = "Ready to run mapped FlaUI scenario matrix rows.";
    [ObservableProperty] private string sourcePathText = string.Empty;
    [ObservableProperty] private bool hasLoadErrors;
    [ObservableProperty] private string loadErrorText = string.Empty;
    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private bool showHigh = true;
    [ObservableProperty] private bool showMedium = true;
    [ObservableProperty] private bool showLow = true;

    public int TotalCount => Items.Count;
    public int MappedCount => Items.Count(item => item.HasMappedTest);
    public int NotImplementedCount => Items.Count(item => !item.HasMappedTest);
    public int PassedCount => Items.Count(item => item.Status == TestScenarioMatrixStatus.Passed);
    public int FailedCount => Items.Count(item => item.Status == TestScenarioMatrixStatus.Failed);
    public int RunningCount => Items.Count(item => item.Status == TestScenarioMatrixStatus.Running);
    public int QueuedCount => Items.Count(item => item.Status == TestScenarioMatrixStatus.Queued);
    public int HighCount => Items.Count(item => item.IsHigh);
    public int MediumCount => Items.Count(item => item.IsMedium);
    public int LowCount => Items.Count(item => item.IsLow);
    public string SummaryText => $"{TotalCount} FlaUI scenarios | {MappedCount} mapped | {NotImplementedCount} not implemented | {HighCount} high | {MediumCount} medium | {LowCount} low | {PassedCount} passed | {FailedCount} failed";

    partial void OnSelectedItemChanged(FlaUiScenarioMatrixItemViewModel? value) => NotifyRunCommands();
    partial void OnIsRunningChanged(bool value) => NotifyRunCommands();
    partial void OnSearchTextChanged(string value) => ItemsView.Refresh();
    partial void OnShowHighChanged(bool value) => ItemsView.Refresh();
    partial void OnShowMediumChanged(bool value) => ItemsView.Refresh();
    partial void OnShowLowChanged(bool value) => ItemsView.Refresh();

    [RelayCommand]
    public void Refresh()
    {
        StatusText = "Loading FlaUI scenario matrix...";
        Items.Clear();

        _document = _loader.Load(_settings);
        SourcePathText = _document.SourcePath;
        HasLoadErrors = _document.LoadErrors.Count > 0;
        LoadErrorText = string.Join(Environment.NewLine, _document.LoadErrors);

        foreach (var item in _document.Items.Select(scenario => new FlaUiScenarioMatrixItemViewModel(scenario)))
            Items.Add(item);

        SelectedItem = Items.FirstOrDefault();
        StatusText = HasLoadErrors
            ? "FlaUI scenario matrix loaded with errors."
            : Items.Count == 0
                ? "No FlaUI scenario matrix rows found."
                : $"FlaUI scenario matrix loaded. {SummaryText}";
        if (!IsRunning)
            RunStatusText = StatusText;

        RefreshCounts();
        ItemsView.Refresh();
        NotifyRunCommands();
    }

    [RelayCommand(CanExecute = nameof(CanRunScenarios))]
    private Task RunAllAsync() => RunScenariosAsync(Items.ToList());

    [RelayCommand(CanExecute = nameof(CanRunScenarios))]
    private Task RunFilteredAsync() => RunScenariosAsync(ItemsView.Cast<FlaUiScenarioMatrixItemViewModel>().ToList());

    [RelayCommand(CanExecute = nameof(CanRunSelectedScenario))]
    private Task RunSelectedAsync()
    {
        if (SelectedItem is null) return Task.CompletedTask;
        return RunScenariosAsync(new[] { SelectedItem }.ToList());
    }

    [RelayCommand]
    private void CopySelectedMarkdown()
    {
        if (SelectedItem is null) return;
        Clipboard.SetText(SelectedItem.Markdown);
    }

    [RelayCommand]
    private void OpenSourceDocument() => OpenPath(SourcePathText);

    private async Task RunScenariosAsync(IReadOnlyList<FlaUiScenarioMatrixItemViewModel> requestedItems)
    {
        var candidates = requestedItems
            .Where(item => !item.IsQueuedOrRunning)
            .DistinctBy(item => item.SpecificScenario)
            .ToList();

        if (candidates.Count == 0)
        {
            RunStatusText = "No eligible FlaUI scenarios to run.";
            return;
        }

        IsRunning = true;
        _runCts = new CancellationTokenSource();
        var startedAt = DateTime.UtcNow;
        var runDir = Path.Combine(_settings.ResultsDirectory, $"FlaUiScenarioMatrixRun_{startedAt:yyyyMMdd_HHmmss}Z");
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
                RunStatusText = "No mapped automated FlaUI tests exist for the selected scenario matrix rows.";
                LogLine?.Invoke("[FLAUI MATRIX] No mapped automated FlaUI tests exist for the selected scenario matrix rows.");
                return;
            }

            var completed = 0;
            foreach (var item in runnable)
            {
                _runCts.Token.ThrowIfCancellationRequested();
                item.Status = TestScenarioMatrixStatus.Running;
                item.FailureDetails = string.Empty;
                RunStatusText = $"Running FlaUI matrix scenario {completed + 1}/{runnable.Count}: {item.SpecificScenario}";
                RefreshCounts();

                var filter = $"FullyQualifiedName~{item.MappedTestFilter}";
                LogLine?.Invoke($"[FLAUI MATRIX] Running {item.SpecificScenario}: {filter}");
                var (exit, trxPath, stdOut) = await _runner.RunFlaUiAsync(
                    $"flaui-matrix-{Slug(item.SpecificScenario)}",
                    filter,
                    runDir,
                    1,
                    new Progress<string>(line => LogLine?.Invoke(line)),
                    _runCts.Token).ConfigureAwait(true);

                item.TrxPath = trxPath;
                item.Output = stdOut;
                var outcomes = _parser.Parse(trxPath, item.SpecificScenario);
                var mappedMethodName = GetMethodName(item.MappedTestFilter);
                var outcome = outcomes.FirstOrDefault(test =>
                    test.TestName.Contains(mappedMethodName, StringComparison.OrdinalIgnoreCase) ||
                    test.TestName.Contains(item.SpecificScenario, StringComparison.OrdinalIgnoreCase));

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

                if (item.Status is TestScenarioMatrixStatus.Failed or TestScenarioMatrixStatus.Blocked)
                    LogLine?.Invoke($"[FLAUI MATRIX] {item.SpecificScenario} {item.StatusLabel}: {item.FailureDetails}");

                completed++;
                RefreshCounts();
                ItemsView.Refresh();
            }

            RunStatusText = $"FlaUI matrix run complete. {PassedCount} passed, {FailedCount} failed, {NotImplementedCount} not implemented.";
        }
        catch (OperationCanceledException)
        {
            foreach (var item in candidates.Where(item => item.IsQueuedOrRunning))
                item.Status = TestScenarioMatrixStatus.Skipped;
            RunStatusText = "FlaUI matrix run cancelled.";
        }
        catch (Exception ex)
        {
            foreach (var item in candidates.Where(item => item.IsQueuedOrRunning))
            {
                item.Status = TestScenarioMatrixStatus.Blocked;
                item.FailureDetails = ex.Message;
            }
            RunStatusText = $"FlaUI matrix run failed: {ex.Message}";
            LogLine?.Invoke($"[FLAUI MATRIX] ERROR: {ex}");
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
        if (obj is not FlaUiScenarioMatrixItemViewModel item) return false;

        var priorityVisible =
            (ShowHigh && item.IsHigh) ||
            (ShowMedium && item.IsMedium) ||
            (ShowLow && item.IsLow);
        if (!priorityVisible) return false;

        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        var haystack = string.Join('\n',
            item.Priority,
            item.ParentScenario,
            item.SpecificScenario,
            item.Description,
            item.PrimaryUiAreasControls,
            item.VerificationTarget,
            item.SuggestedEvidence,
            item.MappedTestFilter,
            item.StatusLabel);
        return haystack.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

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
        OnPropertyChanged(nameof(HighCount));
        OnPropertyChanged(nameof(MediumCount));
        OnPropertyChanged(nameof(LowCount));
        OnPropertyChanged(nameof(SummaryText));
    }

    private bool CanRunScenarios() => !IsRunning && Items.Count > 0;
    private bool CanRunSelectedScenario() => !IsRunning && SelectedItem is not null;

    private void NotifyRunCommands()
    {
        RunAllCommand.NotifyCanExecuteChanged();
        RunFilteredCommand.NotifyCanExecuteChanged();
        RunSelectedCommand.NotifyCanExecuteChanged();
    }

    private static void ApplyOutcome(FlaUiScenarioMatrixItemViewModel item, TestOutcome outcome)
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

    private static string GetMethodName(string mappedTestFilter)
    {
        if (string.IsNullOrWhiteSpace(mappedTestFilter))
            return string.Empty;

        var lastDot = mappedTestFilter.LastIndexOf('.');
        return lastDot >= 0 && lastDot < mappedTestFilter.Length - 1
            ? mappedTestFilter[(lastDot + 1)..]
            : mappedTestFilter;
    }

    private static string Slug(string value)
    {
        var chars = value.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray();
        return string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }
}
