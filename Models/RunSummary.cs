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
    public List<BuildError> BuildErrors { get; } = new();
    public List<GateResult> GateResults { get; } = new();
    public Scorecard Scorecard { get; set; } = new();
    public DeployDecision Decision { get; set; } = DeployDecision.NoGo;
    public string? ReportMarkdownPath { get; set; }
    public string? ReportJsonPath { get; set; }
}

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
