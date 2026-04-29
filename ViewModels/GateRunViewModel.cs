using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SI360.GateRunner.Models;

namespace SI360.GateRunner.ViewModels;

public partial class GateRunViewModel : ObservableObject
{
    public GateRunViewModel(GateDefinition definition)
    {
        Definition = definition;
        Result = new GateResult { Definition = definition };
    }

    public GateDefinition Definition { get; }

    [ObservableProperty] private GateStatus status = GateStatus.Pending;
    [ObservableProperty] private int passed;
    [ObservableProperty] private int failed;
    [ObservableProperty] private int skipped;
    [ObservableProperty] private double durationMs;
    [ObservableProperty] private bool isRunning;

    public GateResult Result { get; }
    public ObservableCollection<TestResultViewModel> Tests { get; } = new();

    public string DisplayName => Definition.DisplayName;
    public string Tooltip => Definition.Notes;
    public string Summary => $"{Passed}/{Passed + Failed + Skipped} passed ({Failed} failed, {Skipped} skipped)";

    public void ApplyResult(GateResult r)
    {
        Passed = r.Passed;
        Failed = r.Failed;
        Skipped = r.Skipped;
        DurationMs = r.Duration.TotalMilliseconds;
        Status = r.Status;
        Tests.Clear();
        foreach (var t in r.Outcomes) Tests.Add(new TestResultViewModel(t));
    }
}
