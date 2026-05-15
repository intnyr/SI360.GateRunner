namespace SI360.GateRunner.Models;

public sealed class FlaUiScenarioMatrixDocument
{
    public string SourcePath { get; set; } = string.Empty;
    public DateTime LoadedAt { get; set; } = DateTime.UtcNow;
    public List<string> LoadErrors { get; } = new();
    public List<FlaUiScenarioMatrixItem> Items { get; } = new();
}

public sealed class FlaUiScenarioMatrixItem
{
    public int SourceOrder { get; set; }
    public string Priority { get; set; } = string.Empty;
    public string ParentScenario { get; set; } = string.Empty;
    public string SpecificScenario { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PrimaryUiAreasControls { get; set; } = string.Empty;
    public string VerificationTarget { get; set; } = string.Empty;
    public string SuggestedEvidence { get; set; } = string.Empty;
    public string? MappedTestFilter { get; set; }
    public string? MappedTestSource { get; set; }
}
