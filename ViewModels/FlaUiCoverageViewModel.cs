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

public sealed partial class FlaUiCoverageViewModel : ObservableObject
{
    private readonly RunnerSettings _settings;
    private readonly IFlaUiCoverageService _coverageService;
    private readonly IFlaUiCoverageRunner _coverageRunner;
    private FlaUiCoverageRun _latestRun = new();
    private CancellationTokenSource? _runCts;

    public FlaUiCoverageViewModel(
        RunnerSettings settings,
        IFlaUiCoverageService coverageService,
        IFlaUiCoverageRunner coverageRunner)
    {
        _settings = settings;
        _coverageService = coverageService;
        _coverageRunner = coverageRunner;
        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = FilterItem;
        ItemsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(FlaUiCoverageItemViewModel.Group)));
        ItemsView.SortDescriptions.Add(new SortDescription(nameof(FlaUiCoverageItemViewModel.Status), ListSortDirection.Ascending));
        Refresh();
    }

    public ObservableCollection<FlaUiCoverageItemViewModel> Items { get; } = new();
    public ICollectionView ItemsView { get; }

    [ObservableProperty] private FlaUiCoverageItemViewModel? selectedItem;
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private string statusText = "FlaUI coverage not loaded.";
    [ObservableProperty] private bool hasLoadErrors;
    [ObservableProperty] private string loadErrorText = string.Empty;
    [ObservableProperty] private bool isRunningCoverage;
    [ObservableProperty] private string runStatusText = "Ready to run mapped FlaUI coverage tests.";
    [ObservableProperty] private bool showOrderTaking = true;
    [ObservableProperty] private bool showUserFunctions = true;
    [ObservableProperty] private bool showPassed = true;
    [ObservableProperty] private bool showFailed = true;
    [ObservableProperty] private bool showBlocked = true;
    [ObservableProperty] private bool showNotStarted = true;
    [ObservableProperty] private bool showNeedsReview = true;

    public int TotalCount => _latestRun.Summary.Total;
    public int AutomatedCount => _latestRun.Summary.Automated;
    public int PassedCount => _latestRun.Summary.Passed;
    public int FailedCount => _latestRun.Summary.Failed;
    public int BlockedCount => _latestRun.Summary.Blocked;
    public int NeedsReviewCount => _latestRun.Summary.NeedsReview;
    public int NotStartedCount => _latestRun.Summary.NotStarted;
    public string SummaryText => $"{TotalCount} scenarios | {AutomatedCount} automated | {PassedCount} passed | {FailedCount} failed | {BlockedCount} blocked | {NeedsReviewCount} needs review | {NotStartedCount} not started";
    public bool HasItems => Items.Count > 0;

    partial void OnSelectedItemChanged(FlaUiCoverageItemViewModel? value)
    {
        RunSelectedCoverageCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsRunningCoverageChanged(bool value)
    {
        RunAllCoverageCommand.NotifyCanExecuteChanged();
        RunFilteredCoverageCommand.NotifyCanExecuteChanged();
        RunSelectedCoverageCommand.NotifyCanExecuteChanged();
        CancelCoverageRunCommand.NotifyCanExecuteChanged();
    }

    partial void OnSearchTextChanged(string value) => ItemsView.Refresh();
    partial void OnShowOrderTakingChanged(bool value) => ItemsView.Refresh();
    partial void OnShowUserFunctionsChanged(bool value) => ItemsView.Refresh();
    partial void OnShowPassedChanged(bool value) => ItemsView.Refresh();
    partial void OnShowFailedChanged(bool value) => ItemsView.Refresh();
    partial void OnShowBlockedChanged(bool value) => ItemsView.Refresh();
    partial void OnShowNotStartedChanged(bool value) => ItemsView.Refresh();
    partial void OnShowNeedsReviewChanged(bool value) => ItemsView.Refresh();

    [RelayCommand]
    public void Refresh()
    {
        StatusText = "Loading FlaUI coverage...";
        Items.Clear();
        _latestRun = _coverageService.Load(_settings);
        HasLoadErrors = _latestRun.LoadErrors.Count > 0;
        LoadErrorText = string.Join(Environment.NewLine, _latestRun.LoadErrors);

        foreach (var item in _latestRun.Sections.SelectMany(s => s.Items).Select(i => new FlaUiCoverageItemViewModel(i)))
            Items.Add(item);

        SelectedItem = Items.FirstOrDefault();
        StatusText = HasLoadErrors
            ? "FlaUI coverage loaded with errors."
            : Items.Count == 0
                ? "No FlaUI coverage scenarios found."
                : $"FlaUI coverage loaded. {SummaryText}";
        if (!IsRunningCoverage)
            RunStatusText = StatusText;
        RefreshCounts();
        ItemsView.Refresh();
        NotifyRunCommands();
    }

    [RelayCommand(CanExecute = nameof(CanRunCoverage))]
    private Task RunAllCoverageAsync() => RunCoverageAsync(new FlaUiCoverageRunRequest());

    [RelayCommand(CanExecute = nameof(CanRunCoverage))]
    private Task RunFilteredCoverageAsync()
    {
        var visibleIds = ItemsView
            .Cast<FlaUiCoverageItemViewModel>()
            .Select(item => item.Result.Item.Id)
            .ToList();
        return RunCoverageAsync(new FlaUiCoverageRunRequest { ItemIds = visibleIds });
    }

    [RelayCommand(CanExecute = nameof(CanRunSelectedCoverage))]
    private Task RunSelectedCoverageAsync()
    {
        if (SelectedItem is null) return Task.CompletedTask;
        return RunCoverageAsync(new FlaUiCoverageRunRequest
        {
            ItemIds = new[] { SelectedItem.Result.Item.Id }
        });
    }

    [RelayCommand(CanExecute = nameof(CanCancelCoverageRun))]
    private void CancelCoverageRun()
    {
        _runCts?.Cancel();
        RunStatusText = "Cancelling FlaUI coverage run...";
    }

    [RelayCommand]
    private void CopySelectedMarkdown()
    {
        if (SelectedItem is null) return;
        Clipboard.SetText(SelectedItem.Markdown);
    }

    [RelayCommand]
    private void OpenSelectedEvidence()
    {
        if (SelectedItem is null) return;
        var first = SelectedItem.Result.EvidencePaths
            .Concat(SelectedItem.Result.Item.SourceFiles)
            .Concat(new[] { SelectedItem.Result.TrxPath })
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
        if (string.IsNullOrWhiteSpace(first)) return;
        OpenPath(ResolvePath(first));
    }

    private async Task RunCoverageAsync(FlaUiCoverageRunRequest request)
    {
        IsRunningCoverage = true;
        _runCts = new CancellationTokenSource();
        try
        {
            RunStatusText = "Starting FlaUI coverage run...";
            var log = new Progress<string>(line => RunStatusText = line);
            var result = await _coverageRunner.RunAsync(request, log, _runCts.Token).ConfigureAwait(true);
            Refresh();

            var skippedText = result.SkippedItems.Count == 0
                ? string.Empty
                : $" {result.SkippedItems.Count} scenario(s) skipped.";
            var errorText = result.Errors.Count == 0
                ? string.Empty
                : $" {string.Join(" ", result.Errors)}";

            RunStatusText = result.StartedProcess
                ? $"FlaUI coverage run finished: exit {result.ExitCode}, {result.MatchedCount} matched, {result.RunnableCount} filters. TRX: {result.TrxPath}.{skippedText}{errorText}"
                : $"FlaUI coverage run did not start: {errorText}{skippedText}";
        }
        catch (OperationCanceledException)
        {
            RunStatusText = "FlaUI coverage run cancelled.";
        }
        catch (Exception ex)
        {
            RunStatusText = $"FlaUI coverage run failed: {ex.Message}";
        }
        finally
        {
            _runCts?.Dispose();
            _runCts = null;
            IsRunningCoverage = false;
        }
    }

    private bool FilterItem(object obj)
    {
        if (obj is not FlaUiCoverageItemViewModel item) return false;
        if (!ShowOrderTaking && item.Result.Item.Group == FlaUiCoverageGroup.OrderTakingProcedures) return false;
        if (!ShowUserFunctions && item.Result.Item.Group == FlaUiCoverageGroup.UserFunctionsAndDiningRoomScenarios) return false;

        var statusVisible = item.Result.Status switch
        {
            FlaUiCoverageStatus.Passed => ShowPassed,
            FlaUiCoverageStatus.Failed => ShowFailed,
            FlaUiCoverageStatus.Blocked => ShowBlocked,
            FlaUiCoverageStatus.NotStarted => ShowNotStarted,
            FlaUiCoverageStatus.NeedsReview => ShowNeedsReview,
            _ => true
        };
        if (!statusVisible) return false;

        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        var haystack = string.Join('\n', item.Scenario, item.Group, item.Status, item.Automation, item.Execution,
            item.LatestTest, item.SourceFiles, item.Evidence, item.Notes, item.Blocker, item.Error);
        return haystack.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    private string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path)) return path;
        return FlaUiCoverageManifestLoader.ResolveSi360Path(_settings, path);
    }

    private static void OpenPath(string path)
    {
        try
        {
            var target = Directory.Exists(path)
                ? path
                : File.Exists(path)
                    ? path
                    : Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(target)) return;
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch
        {
            // Opening evidence is best-effort; the row still carries the path for copy/report use.
        }
    }

    private void RefreshCounts()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(AutomatedCount));
        OnPropertyChanged(nameof(PassedCount));
        OnPropertyChanged(nameof(FailedCount));
        OnPropertyChanged(nameof(BlockedCount));
        OnPropertyChanged(nameof(NeedsReviewCount));
        OnPropertyChanged(nameof(NotStartedCount));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(HasItems));
    }

    private bool CanRunCoverage() => !IsRunningCoverage && Items.Count > 0;
    private bool CanRunSelectedCoverage() => !IsRunningCoverage && SelectedItem is not null;
    private bool CanCancelCoverageRun() => IsRunningCoverage;

    private void NotifyRunCommands()
    {
        RunAllCoverageCommand.NotifyCanExecuteChanged();
        RunFilteredCoverageCommand.NotifyCanExecuteChanged();
        RunSelectedCoverageCommand.NotifyCanExecuteChanged();
        CancelCoverageRunCommand.NotifyCanExecuteChanged();
    }
}
