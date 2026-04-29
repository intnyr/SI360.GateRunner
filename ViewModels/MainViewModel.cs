using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Data;
using Clipboard = System.Windows.Clipboard;
using MessageBox = System.Windows.MessageBox;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SI360.GateRunner.Models;
using SI360.GateRunner.Services;

namespace SI360.GateRunner.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly RunnerSettings _settings;
    private readonly DotnetTestRunner _runner;
    private readonly BuildErrorCollector _build;
    private readonly TrxResultParser _parser;
    private readonly ScorecardAggregator _aggregator;
    private readonly ReportWriter _reportWriter;
    private readonly ThemeManager _themeManager;
    private readonly ToastNotifier _toast;
    private CancellationTokenSource? _cts;
    private readonly StringBuilder _logBuffer = new();
    private readonly object _logLock = new();
    private const int MaxLogChars = 512 * 1024;
    private DateTime _runStartedAt;
    private readonly System.Windows.Threading.DispatcherTimer _etaTimer;

    public MainViewModel(
        RunnerSettings settings,
        DotnetTestRunner runner,
        BuildErrorCollector build,
        TrxResultParser parser,
        ScorecardAggregator aggregator,
        ReportWriter reportWriter,
        ThemeManager themeManager,
        ToastNotifier toast)
    {
        _settings = settings;
        _runner = runner;
        _build = build;
        _parser = parser;
        _aggregator = aggregator;
        _reportWriter = reportWriter;
        _themeManager = themeManager;
        _toast = toast;

        foreach (var def in GateCatalog.All)
            Gates.Add(new GateRunViewModel(def));

        Scorecard = new ScorecardViewModel();

        GatesView = CollectionViewSource.GetDefaultView(Gates);
        GatesView.Filter = FilterGate;

        FailuresView = CollectionViewSource.GetDefaultView(Failures);
        FailuresView.SortDescriptions.Add(new SortDescription(nameof(FailureItemViewModel.SeverityRank), ListSortDirection.Ascending));
        FailuresView.SortDescriptions.Add(new SortDescription(nameof(FailureItemViewModel.GateName), ListSortDirection.Ascending));

        _etaTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _etaTimer.Tick += (_, _) => RecomputeEta();
    }

    public ObservableCollection<GateRunViewModel> Gates { get; } = new();
    public ObservableCollection<FailureItemViewModel> Failures { get; } = new();
    public ObservableCollection<BuildError> BuildErrors { get; } = new();
    public ScorecardViewModel Scorecard { get; }
    public ICollectionView GatesView { get; }
    public ICollectionView FailuresView { get; }

    [ObservableProperty] private string logTail = string.Empty;
    [ObservableProperty] private string statusText = "Idle.";
    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private int completedGates;
    [ObservableProperty] private int totalGates = GateCatalog.All.Count;
    [ObservableProperty] private string? latestReportPath;
    [ObservableProperty] private GateRunViewModel? selectedGate;
    [ObservableProperty] private string solutionPath = string.Empty;
    [ObservableProperty] private string testProjectPath = string.Empty;

    // Filter chips
    [ObservableProperty] private bool showRed = true;
    [ObservableProperty] private bool showYellow = true;
    [ObservableProperty] private bool showGreen = true;
    [ObservableProperty] private bool showPending = true;

    partial void OnShowRedChanged(bool value) => GatesView.Refresh();
    partial void OnShowYellowChanged(bool value) => GatesView.Refresh();
    partial void OnShowGreenChanged(bool value) => GatesView.Refresh();
    partial void OnShowPendingChanged(bool value) => GatesView.Refresh();

    // #1 Chip counts
    public int RedCount => Gates.Count(g => g.Status is GateStatus.Red or GateStatus.Error);
    public int YellowCount => Gates.Count(g => g.Status == GateStatus.Yellow);
    public int GreenCount => Gates.Count(g => g.Status == GateStatus.Green);
    public int PendingCount => Gates.Count(g => g.Status is GateStatus.Pending or GateStatus.Running);

    public string RedChipLabel => $"● Red ({RedCount})";
    public string YellowChipLabel => $"● Yellow ({YellowCount})";
    public string GreenChipLabel => $"● Green ({GreenCount})";
    public string PendingChipLabel => $"○ Pending ({PendingCount})";

    private void RefreshChipCounts()
    {
        OnPropertyChanged(nameof(RedCount));
        OnPropertyChanged(nameof(YellowCount));
        OnPropertyChanged(nameof(GreenCount));
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(RedChipLabel));
        OnPropertyChanged(nameof(YellowChipLabel));
        OnPropertyChanged(nameof(GreenChipLabel));
        OnPropertyChanged(nameof(PendingChipLabel));
    }

    // #6 Chip toggle commands (keyboard)
    [RelayCommand] private void ToggleShowRed() => ShowRed = !ShowRed;
    [RelayCommand] private void ToggleShowYellow() => ShowYellow = !ShowYellow;
    [RelayCommand] private void ToggleShowGreen() => ShowGreen = !ShowGreen;
    [RelayCommand] private void ToggleShowPending() => ShowPending = !ShowPending;

    // Score delta (#1)
    [ObservableProperty] private string previousRunText = "No previous run.";
    [ObservableProperty] private string scoreDeltaText = string.Empty;
    [ObservableProperty] private double scoreDelta;

    // ETA (#8)
    [ObservableProperty] private string etaText = string.Empty;

    // Theme (#9)
    [ObservableProperty] private string themeToggleText = "☀ Light";

    public void RefreshSettings()
    {
        SolutionPath = _settings.SolutionPath;
        TestProjectPath = _settings.TestProjectPath;
        LoadPreviousRun();
    }

    private void LoadPreviousRun()
    {
        var prev = PreviousRunLoader.LoadLatest(_settings.ResultsDirectory);
        if (prev is null)
        {
            PreviousRunText = "No previous run.";
            ScoreDeltaText = string.Empty;
            return;
        }
        PreviousRunText = $"Last run {prev.StartedAt:yyyy-MM-dd HH:mm} → {prev.OverallScore:0.0} ({prev.Grade}, {prev.Decision})";
    }

    private bool FilterGate(object obj)
    {
        if (obj is not GateRunViewModel g) return true;
        return g.Status switch
        {
            GateStatus.Red or GateStatus.Error => ShowRed,
            GateStatus.Yellow => ShowYellow,
            GateStatus.Green => ShowGreen,
            _ => ShowPending
        };
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RunGatesAsync() => ExecuteRunAsync(singleGate: null);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RerunGateAsync(GateRunViewModel? gate)
    {
        if (gate is null) return Task.CompletedTask;
        return ExecuteRunAsync(singleGate: gate);
    }

    private async Task ExecuteRunAsync(GateRunViewModel? singleGate)
    {
        IsRunning = true;
        RunGatesCommand.NotifyCanExecuteChanged();
        RerunGateCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        _cts = new CancellationTokenSource();
        var log = new Progress<string>(AppendLog);

        _runStartedAt = DateTime.Now;
        _etaTimer.Start();

        try
        {
            ClearPreviousRun(singleGate);
            LoadPreviousRun();
            var startedAt = _runStartedAt;
            var summary = new RunSummary { StartedAt = startedAt };

            if (string.IsNullOrWhiteSpace(_settings.SolutionPath) || !File.Exists(_settings.SolutionPath))
            {
                StatusText = $"Solution not found at '{_settings.SolutionPath}'. Configure in settings.";
                return;
            }

            var gatesToRun = singleGate is null ? Gates.ToList() : new List<GateRunViewModel> { singleGate };
            TotalGates = gatesToRun.Count;

            if (singleGate is null)
            {
                StatusText = "Restoring…";
                await _runner.RestoreAsync(log, _cts.Token);

                StatusText = "Building (Release)…";
                var (buildExit, errors) = await _build.BuildAsync(log, _cts.Token);
                foreach (var e in errors) BuildErrors.Add(e);
                summary.BuildErrors.AddRange(errors);

                if (buildExit != 0 || errors.Count > 0)
                {
                    StatusText = $"Build failed: {errors.Count} errors. Deploy decision = NO-GO.";
                    summary.Decision = DeployDecision.NoGo;
                    await FinalizeAsync(summary, startedAt);
                    _toast.Show("Gate Run — NO-GO", $"{errors.Count} build errors. Deploy blocked.", error: true);
                    return;
                }
            }
            else
            {
                StatusText = $"Re-running {singleGate.DisplayName}…";
            }

            var runDir = Path.Combine(_settings.ResultsDirectory, $"GateRun_{startedAt:yyyyMMdd_HHmmss}");
            Directory.CreateDirectory(runDir);

            foreach (var gateVm in gatesToRun)
            {
                if (_cts.Token.IsCancellationRequested) break;
                gateVm.IsRunning = true;
                gateVm.Status = GateStatus.Running;
                GatesView.Refresh();
                StatusText = $"Running {gateVm.DisplayName} ({CompletedGates + 1}/{TotalGates})…";

                var sw = Stopwatch.StartNew();
                var (exit, trxPath, stdOut) = await _runner.RunGateAsync(
                    gateVm.Definition.Id,
                    gateVm.Definition.TestClassFilter,
                    runDir,
                    log,
                    _cts.Token);
                sw.Stop();

                var outcomes = _parser.Parse(trxPath, gateVm.Definition.Id);
                var result = gateVm.Result;
                result.Duration = sw.Elapsed;
                result.TrxPath = trxPath;
                result.StdOut = stdOut;
                result.Outcomes.Clear();
                result.Outcomes.AddRange(outcomes);
                result.Passed = outcomes.Count(o => o.Status == TestStatus.Passed);
                result.Failed = outcomes.Count(o => o.Status == TestStatus.Failed);
                result.Skipped = outcomes.Count(o => o.Status == TestStatus.Skipped);
                if (result.Total == 0 && exit != 0) result.ErrorMessage = $"dotnet test exit {exit}; no TRX parsed.";
                result.ComputeStatus();

                gateVm.IsRunning = false;
                gateVm.ApplyResult(result);
                summary.GateResults.Add(result);

                foreach (var f in outcomes.Where(o => o.Status == TestStatus.Failed))
                    Failures.Add(FailureItemViewModel.From(f));

                CompletedGates++;
                GatesView.Refresh();
                RefreshChipCounts();
            }

            if (singleGate is null)
            {
                summary.Scorecard = _aggregator.Build(summary.GateResults);
                summary.Decision = _aggregator.Decide(summary);
                Scorecard.Apply(summary.Scorecard, summary.Decision);

                var prev = PreviousRunLoader.LoadLatest(_settings.ResultsDirectory);
                await FinalizeAsync(summary, startedAt);
                ComputeScoreDelta(prev, summary.Scorecard.OverallScore);

                StatusText = $"Done. Decision: {Scorecard.DecisionText}. Score {summary.Scorecard.OverallScore:0.0} ({summary.Scorecard.Grade}).";
                _toast.Show(
                    $"Gate Run — {Scorecard.DecisionText}",
                    $"Overall {summary.Scorecard.OverallScore:0.0} ({summary.Scorecard.Grade}). {Failures.Count} failures.",
                    error: summary.Decision != DeployDecision.Go);
            }
            else
            {
                StatusText = $"Re-ran {singleGate.DisplayName}: {singleGate.Passed} passed, {singleGate.Failed} failed.";
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            AppendLog($"[ERROR] {ex}");
        }
        finally
        {
            _etaTimer.Stop();
            EtaText = string.Empty;
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
            RunGatesCommand.NotifyCanExecuteChanged();
            RerunGateCommand.NotifyCanExecuteChanged();
            CancelCommand.NotifyCanExecuteChanged();
        }
    }

    private void ComputeScoreDelta(PreviousRun? prev, double current)
    {
        if (prev is null) { ScoreDeltaText = string.Empty; return; }
        var delta = Math.Round(current - prev.OverallScore, 2);
        ScoreDelta = delta;
        var sign = delta > 0 ? "▲ +" : delta < 0 ? "▼ " : "= ";
        ScoreDeltaText = $"{sign}{delta:0.00} since {prev.StartedAt:yyyy-MM-dd HH:mm}";
    }

    private void RecomputeEta()
    {
        if (!IsRunning || CompletedGates == 0) { EtaText = "…"; return; }
        var elapsed = DateTime.Now - _runStartedAt;
        var avgPerGate = elapsed.TotalSeconds / CompletedGates;
        var remaining = Math.Max(0, TotalGates - CompletedGates);
        var etaSec = avgPerGate * remaining;
        EtaText = $"ETA ~{FormatDuration(TimeSpan.FromSeconds(etaSec))} · elapsed {FormatDuration(elapsed)}";
    }

    private static string FormatDuration(TimeSpan t)
        => t.TotalHours >= 1 ? $"{(int)t.TotalHours}h{t.Minutes:D2}m" :
           t.TotalMinutes >= 1 ? $"{t.Minutes}m{t.Seconds:D2}s" :
           $"{t.Seconds}s";

    private async Task FinalizeAsync(RunSummary summary, DateTime startedAt)
    {
        summary.Duration = DateTime.Now - startedAt;
        var (md, json) = await _reportWriter.WriteAsync(summary, _settings.ResultsDirectory);
        summary.ReportMarkdownPath = md;
        summary.ReportJsonPath = json;
        LatestReportPath = md;
        OpenReportCommand.NotifyCanExecuteChanged();
    }

    private bool CanRun() => !IsRunning;
    private bool CanCancel() => IsRunning;
    private bool CanOpenReport() => !string.IsNullOrEmpty(LatestReportPath) && File.Exists(LatestReportPath);

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cts?.Cancel();
        StatusText = "Cancelling…";
    }

    [RelayCommand(CanExecute = nameof(CanOpenReport))]
    private void OpenReport()
    {
        if (string.IsNullOrEmpty(LatestReportPath)) return;
        var path = LatestReportPath;
        if (path.Contains(' '))
        {
            var confirm = MessageBox.Show(
                $"Open report?\n\n{path}",
                "SI360 Gate Runner",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.OK) return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText = $"Open failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CopyFailureMarkdown(FailureItemViewModel? item)
    {
        if (item is null) return;
        try { Clipboard.SetText(item.ToMarkdown()); StatusText = "Failure copied as Markdown."; }
        catch (Exception ex) { StatusText = $"Clipboard failed: {ex.Message}"; }
    }

    [RelayCommand]
    private void CopyAllFailuresMarkdown()
    {
        if (Failures.Count == 0) return;
        var sb = new StringBuilder();
        sb.AppendLine($"## Failure Inventory ({Failures.Count})\n");
        var n = 1;
        foreach (var f in Failures)
        {
            sb.Append(n++).Append(". ");
            sb.AppendLine(f.ToMarkdown());
        }
        try { Clipboard.SetText(sb.ToString()); StatusText = $"Copied {Failures.Count} failures as Markdown."; }
        catch (Exception ex) { StatusText = $"Clipboard failed: {ex.Message}"; }
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        _themeManager.Toggle();
        ThemeToggleText = _themeManager.Current == AppTheme.Dark ? "☀ Light" : "🌙 Dark";
    }

    [RelayCommand]
    private void SwitchTab(string? indexStr)
    {
        if (!int.TryParse(indexStr, out var idx)) return;
        TabSwitchRequested?.Invoke(idx);
    }

    public event Action<int>? TabSwitchRequested;

    private void ClearPreviousRun(GateRunViewModel? singleGate)
    {
        if (singleGate is null)
        {
            CompletedGates = 0;
            Failures.Clear();
            BuildErrors.Clear();
            foreach (var g in Gates) ResetGate(g);
        }
        else
        {
            CompletedGates = 0;
            for (var i = Failures.Count - 1; i >= 0; i--)
                if (Failures[i].GateName == singleGate.Definition.Id) Failures.RemoveAt(i);
            ResetGate(singleGate);
        }
        lock (_logLock) { _logBuffer.Clear(); LogTail = string.Empty; }
        LatestReportPath = null;
        OpenReportCommand.NotifyCanExecuteChanged();
        GatesView.Refresh();
        RefreshChipCounts();
    }

    private static void ResetGate(GateRunViewModel g)
    {
        g.Status = GateStatus.Pending;
        g.Passed = g.Failed = g.Skipped = 0;
        g.DurationMs = 0;
        g.Tests.Clear();
        g.Result.Outcomes.Clear();
        g.Result.Passed = g.Result.Failed = g.Result.Skipped = 0;
        g.Result.StdOut = null;
        g.Result.ErrorMessage = null;
    }

    private void AppendLog(string line)
    {
        lock (_logLock)
        {
            _logBuffer.AppendLine(line);
            if (_logBuffer.Length > MaxLogChars)
                _logBuffer.Remove(0, _logBuffer.Length - MaxLogChars);
            LogTail = _logBuffer.ToString();
        }
    }
}
