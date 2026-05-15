using CommunityToolkit.Mvvm.ComponentModel;
using SI360.GateRunner.Models;

namespace SI360.GateRunner.ViewModels;

public sealed partial class FlaUiScenarioMatrixItemViewModel : ObservableObject
{
    public FlaUiScenarioMatrixItemViewModel(FlaUiScenarioMatrixItem item)
    {
        Item = item;
        Status = TestScenarioMatrixStatus.NotRun;
    }

    public FlaUiScenarioMatrixItem Item { get; }
    public int SourceOrder => Item.SourceOrder;

    [ObservableProperty] private TestScenarioMatrixStatus status;
    [ObservableProperty] private string failureDetails = string.Empty;
    [ObservableProperty] private string output = string.Empty;
    [ObservableProperty] private string trxPath = string.Empty;

    public string Priority => Item.Priority;
    public string ParentScenario => Item.ParentScenario;
    public string SpecificScenario => Item.SpecificScenario;
    public string Description => Item.Description;
    public string PrimaryUiAreasControls => Item.PrimaryUiAreasControls;
    public string VerificationTarget => Item.VerificationTarget;
    public string SuggestedEvidence => Item.SuggestedEvidence;
    public string MappedTestFilter => Item.MappedTestFilter ?? string.Empty;
    public string MappedTestSource => Item.MappedTestSource ?? string.Empty;
    public bool HasMappedTest => !string.IsNullOrWhiteSpace(Item.MappedTestFilter);
    public bool IsQueuedOrRunning => Status is TestScenarioMatrixStatus.Queued or TestScenarioMatrixStatus.Running;
    public string StatusLabel => Status switch
    {
        TestScenarioMatrixStatus.NotRun => "Not Run",
        TestScenarioMatrixStatus.Queued => "Queued",
        TestScenarioMatrixStatus.Running => "Running",
        TestScenarioMatrixStatus.Passed => "Passed",
        TestScenarioMatrixStatus.Failed => "Failed",
        TestScenarioMatrixStatus.Blocked => "Blocked",
        TestScenarioMatrixStatus.Skipped => "Skipped",
        TestScenarioMatrixStatus.NotImplemented => "Not Implemented",
        _ => Status.ToString()
    };

    public int PriorityRank => Priority switch
    {
        "High" => 0,
        "Medium" => 1,
        "Low" => 2,
        _ => 3
    };

    public bool IsHigh => Priority.Equals("High", StringComparison.OrdinalIgnoreCase);
    public bool IsMedium => Priority.Equals("Medium", StringComparison.OrdinalIgnoreCase);
    public bool IsLow => Priority.Equals("Low", StringComparison.OrdinalIgnoreCase);

    public string Markdown => string.Join(Environment.NewLine,
        $"### {SpecificScenario}",
        $"- Status: {StatusLabel}",
        $"- Priority: {Priority}",
        $"- Parent Scenario: {ParentScenario}",
        $"- Mapped Test: {(HasMappedTest ? MappedTestFilter : "Not Implemented")}",
        $"- Description: {Description}",
        $"- Primary UI Areas / Controls: {PrimaryUiAreasControls}",
        $"- Verification Target: {VerificationTarget}",
        $"- Suggested Evidence: {SuggestedEvidence}",
        string.IsNullOrWhiteSpace(FailureDetails) ? string.Empty : $"- Failure Details: {FailureDetails}").Trim();

    partial void OnStatusChanged(TestScenarioMatrixStatus value)
    {
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(IsQueuedOrRunning));
        OnPropertyChanged(nameof(Markdown));
    }

    partial void OnFailureDetailsChanged(string value) => OnPropertyChanged(nameof(Markdown));
}
