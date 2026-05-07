using CommunityToolkit.Mvvm.ComponentModel;
using SI360.GateRunner.Models;

namespace SI360.GateRunner.ViewModels;

public sealed partial class FlaUiCoverageItemViewModel : ObservableObject
{
    public FlaUiCoverageItemViewModel(FlaUiCoverageItemResult result)
    {
        Result = result;
    }

    public FlaUiCoverageItemResult Result { get; }
    public string Id => Result.Item.Id;
    public string Scenario => Result.Item.Name;
    public string Group => Result.Item.Group == FlaUiCoverageGroup.OrderTakingProcedures
        ? "ORDER TAKING PROCEDURES"
        : "USER FUNCTIONS AND DINING ROOM SCENARIOS";
    public string Status => Label(Result.Status);
    public string Automation => Result.Item.AutomationStatus.ToString();
    public string Execution => Result.ExecutionStatus.ToString();
    public string VerificationLevel => Result.Item.VerificationLevel;
    public string LatestTest => Result.LatestTestName ?? string.Empty;
    public string SourceFiles => string.Join("; ", Result.Item.SourceFiles);
    public string Evidence => string.Join("; ", Result.EvidencePaths.Distinct(StringComparer.OrdinalIgnoreCase));
    public string Notes => Result.Item.Notes;
    public string Blocker => Result.BlockerReason ?? string.Empty;
    public string Error => Result.ErrorMessage ?? string.Empty;
    public bool HasEvidence => Result.EvidencePaths.Count > 0 || !string.IsNullOrWhiteSpace(Result.TrxPath);
    public bool IsOpenable => HasEvidence;

    public string Markdown => $"""
        ### {Scenario}
        - Coverage Group: {Group}
        - Status: {Status}
        - Automation: {Automation}
        - Execution: {Execution}
        - Verification Level: {VerificationLevel}
        - Latest Test: {LatestTest}
        - Source Files: {SourceFiles}
        - Evidence: {Evidence}
        - Blocker: {Blocker}
        - Error: {Error}
        - Notes: {Notes}
        """;

    private static string Label(FlaUiCoverageStatus status) => status switch
    {
        FlaUiCoverageStatus.NotStarted => "Not Started",
        FlaUiCoverageStatus.NeedsReview => "Needs Review",
        _ => status.ToString()
    };
}
