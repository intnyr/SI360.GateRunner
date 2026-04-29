namespace SI360.GateRunner.Models;

public enum DeployDecision
{
    Go,
    Hold,
    NoGo
}

public sealed class RunSummary
{
    public required DateTime StartedAt { get; init; }
    public TimeSpan Duration { get; set; }
    public RunEnvironment Environment { get; set; } = new();
    public List<BuildError> BuildErrors { get; } = new();
    public List<GateResult> GateResults { get; } = new();
    public List<GateCatalogWarning> GateCatalogWarnings { get; } = new();
    public Scorecard Scorecard { get; set; } = new();
    public DeployDecision Decision { get; set; } = DeployDecision.NoGo;
    public string DecisionPolicyName { get; set; } = string.Empty;
    public string DecisionPolicyVersion { get; set; } = string.Empty;
    public string DecisionRationale { get; set; } = string.Empty;
    public ReportHistoryComparison History { get; set; } = new();
    public string? ReportMarkdownPath { get; set; }
    public string? ReportJsonPath { get; set; }
}

public sealed class ReportHistoryComparison
{
    public bool PriorRunFound { get; set; }
    public string? PriorReportPath { get; set; }
    public DateTime? PriorStartedAt { get; set; }
    public List<FailureFingerprint> NewFailures { get; } = new();
    public List<FailureFingerprint> RecurringFailures { get; } = new();
    public List<FailureFingerprint> ResolvedFailures { get; } = new();
}

public sealed record FailureFingerprint(
    string GateName,
    string TestName,
    string? FilePath,
    int? LineNumber);

public sealed class Scorecard
{
    public double ScenarioScore { get; set; }
    public double ProbabilisticScore { get; set; }
    public double OverallScore => Math.Round(ScenarioScore * 0.6 + ProbabilisticScore * 0.4, 2);
    public string Grade => OverallScore switch
    {
        >= 95 => "A+",
        >= 90 => "A",
        >= 80 => "B+",
        >= 70 => "B",
        >= 60 => "C",
        _ => "F"
    };
    public List<ScenarioPass> Scenarios { get; } = new();
    public List<ProbabilisticEntry> Probabilistics { get; } = new();
}

public sealed record ScenarioPass(string Name, int Weight, bool Passed);

public sealed record ProbabilisticEntry(
    string Name,
    double SuccessRatePct,
    int Score,
    double? P50Ms,
    double? P95Ms,
    double? P99Ms);

public sealed record GateCatalogWarning(string Code, string Message);

public sealed class RunEnvironment
{
    public string ToolVersion { get; set; } = typeof(RunEnvironment).Assembly.GetName().Version?.ToString() ?? "unknown";
    public string MachineName { get; set; } = System.Environment.MachineName;
    public string OSVersion { get; set; } = System.Environment.OSVersion.VersionString;
    public string RuntimeVersion { get; set; } = System.Environment.Version.ToString();
    public string DotnetSdkVersion { get; set; } = string.Empty;
    public string RepositoryPath { get; set; } = string.Empty;
    public string Branch { get; set; } = string.Empty;
    public string Commit { get; set; } = string.Empty;
    public string ArtifactDirectory { get; set; } = string.Empty;
    public List<ProcessCommandSnapshot> Commands { get; } = new();
}

public sealed record ProcessCommandSnapshot(string Name, string CommandLine, double TimeoutSeconds);
