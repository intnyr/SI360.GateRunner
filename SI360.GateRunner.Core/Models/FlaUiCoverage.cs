namespace SI360.GateRunner.Models;

public enum FlaUiCoverageGroup
{
    OrderTakingProcedures,
    UserFunctionsAndDiningRoomScenarios
}

public enum FlaUiAutomationStatus
{
    Automated,
    PartiallyAutomated,
    NotAutomated,
    NeedsReview
}

public enum FlaUiExecutionStatus
{
    Passed,
    Failed,
    Blocked,
    NotRun,
    Unknown
}

public enum FlaUiCoverageStatus
{
    NotStarted,
    Passed,
    Failed,
    Blocked,
    NeedsReview
}

public sealed class FlaUiCoverageManifest
{
    public string SchemaVersion { get; set; } = "1.0";
    public List<FlaUiCoverageSection> Sections { get; set; } = new();
}

public sealed class FlaUiCoverageSection
{
    public FlaUiCoverageGroup Group { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? SourceDocument { get; set; }
    public List<FlaUiCoverageItem> Items { get; set; } = new();
}

public sealed class FlaUiCoverageItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public FlaUiCoverageGroup Group { get; set; }
    public FlaUiAutomationStatus AutomationStatus { get; set; } = FlaUiAutomationStatus.NotAutomated;
    public string VerificationLevel { get; set; } = "None";
    public List<string> TestFilters { get; set; } = new();
    public List<string> SourceFiles { get; set; } = new();
    public List<string> EvidencePaths { get; set; } = new();
    public string Notes { get; set; } = string.Empty;
}

public sealed class FlaUiCoverageValidationResult
{
    public FlaUiCoverageManifest? Manifest { get; set; }
    public List<string> Errors { get; } = new();
    public bool IsValid => Manifest is not null && Errors.Count == 0;
}

public sealed class FlaUiCoverageRun
{
    public string SchemaVersion { get; set; } = "1.0";
    public DateTime LoadedAt { get; set; } = DateTime.UtcNow;
    public string? ManifestPath { get; set; }
    public List<string> LoadErrors { get; } = new();
    public List<FlaUiCoverageSectionResult> Sections { get; } = new();
    public FlaUiCoverageSummary Summary { get; set; } = new();
}

public sealed class FlaUiCoverageSectionResult
{
    public FlaUiCoverageGroup Group { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? SourceDocument { get; set; }
    public List<FlaUiCoverageItemResult> Items { get; } = new();
}

public sealed class FlaUiCoverageItemResult
{
    public required FlaUiCoverageItem Item { get; init; }
    public FlaUiCoverageStatus Status { get; set; } = FlaUiCoverageStatus.NotStarted;
    public FlaUiExecutionStatus ExecutionStatus { get; set; } = FlaUiExecutionStatus.NotRun;
    public string? LatestTestName { get; set; }
    public string? TrxPath { get; set; }
    public string? ErrorMessage { get; set; }
    public string? BlockerReason { get; set; }
    public List<string> EvidencePaths { get; } = new();
}

public sealed class FlaUiCoverageSummary
{
    public int Total { get; set; }
    public int Automated { get; set; }
    public int Passed { get; set; }
    public int Failed { get; set; }
    public int Blocked { get; set; }
    public int NeedsReview { get; set; }
    public int NotStarted { get; set; }
}
