using CommunityToolkit.Mvvm.ComponentModel;
using SI360.GateRunner.Models;

namespace SI360.GateRunner.ViewModels;

public partial class TestResultViewModel : ObservableObject
{
    public TestResultViewModel(TestOutcome outcome)
    {
        Outcome = outcome;
    }

    public TestOutcome Outcome { get; }
    public string Name => Outcome.TestName;
    public string Status => Outcome.Status.ToString();
    public double DurationMs => Outcome.Duration.TotalMilliseconds;
    public string ErrorExcerpt => Outcome.ErrorMessage ?? string.Empty;
    public string? Location => Outcome.FilePath is null ? null : $"{Outcome.FilePath}:{Outcome.LineNumber}";
}
