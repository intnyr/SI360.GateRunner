using CommunityToolkit.Mvvm.ComponentModel;
using SI360.GateRunner.Models;

namespace SI360.GateRunner.ViewModels;

public sealed partial class TestScenarioMatrixItemViewModel : ObservableObject
{
    public TestScenarioMatrixItemViewModel(TestScenarioMatrixItem item)
    {
        Item = item;
        Status = TestScenarioMatrixStatus.NotRun;
    }

    public TestScenarioMatrixItem Item { get; }

    [ObservableProperty] private TestScenarioMatrixStatus status;
    [ObservableProperty] private string failureDetails = string.Empty;
    [ObservableProperty] private string output = string.Empty;
    [ObservableProperty] private string trxPath = string.Empty;

    public string Area => Item.Area;
    public string Scenario => Item.Scenario;
    public string RecommendedTestType => Item.RecommendedTestType;
    public string SetupInputs => Item.SetupInputs;
    public string ExpectedResult => Item.ExpectedResult;
    public string SuggestedTargetCode => Item.SuggestedTargetCode;
    public string Notes => Item.Notes;
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

    public bool IsUnit => HasType("unit");
    public bool IsService => HasType("service");
    public bool IsRepository => HasType("repository");
    public bool IsIntegration => HasType("integration");
    public bool IsStatic => HasType("static") || HasType("architecture");
    public bool IsOther => !IsUnit && !IsService && !IsRepository && !IsIntegration && !IsStatic;

    public string Markdown => string.Join(Environment.NewLine,
        $"### {Scenario}",
        $"- Status: {StatusLabel}",
        $"- Area: {Area}",
        $"- Recommended Test Type: {RecommendedTestType}",
        $"- Mapped Test: {(HasMappedTest ? MappedTestFilter : "Not Implemented")}",
        $"- Setup / Inputs: {SetupInputs}",
        $"- Expected Result: {ExpectedResult}",
        $"- Suggested Target Code: {SuggestedTargetCode}",
        $"- Notes: {Notes}",
        string.IsNullOrWhiteSpace(FailureDetails) ? string.Empty : $"- Failure Details: {FailureDetails}").Trim();

    partial void OnStatusChanged(TestScenarioMatrixStatus value)
    {
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(IsQueuedOrRunning));
        OnPropertyChanged(nameof(Markdown));
    }

    partial void OnFailureDetailsChanged(string value) => OnPropertyChanged(nameof(Markdown));

    private bool HasType(string token) =>
        RecommendedTestType.Contains(token, StringComparison.OrdinalIgnoreCase);
}

public enum TestScenarioMatrixStatus
{
    NotRun,
    Queued,
    Running,
    Passed,
    Failed,
    Blocked,
    Skipped,
    NotImplemented
}
