namespace SI360.GateRunner.Models;

public sealed class TestScenarioMatrixDocument
{
    public string SourcePath { get; set; } = string.Empty;
    public DateTime LoadedAt { get; set; } = DateTime.UtcNow;
    public List<string> LoadErrors { get; } = new();
    public List<TestScenarioMatrixItem> Items { get; } = new();
}

public sealed class TestScenarioMatrixItem
{
    public string Area { get; set; } = string.Empty;
    public string Scenario { get; set; } = string.Empty;
    public string RecommendedTestType { get; set; } = string.Empty;
    public string SetupInputs { get; set; } = string.Empty;
    public string ExpectedResult { get; set; } = string.Empty;
    public string SuggestedTargetCode { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string? MappedTestFilter { get; set; }
    public string? MappedTestSource { get; set; }
}
