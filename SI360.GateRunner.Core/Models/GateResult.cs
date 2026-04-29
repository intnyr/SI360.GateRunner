namespace SI360.GateRunner.Models;

public enum GateStatus
{
    Pending,
    Running,
    Green,
    Yellow,
    Red,
    Error
}

public sealed class GateResult
{
    public required GateDefinition Definition { get; init; }
    public GateStatus Status { get; set; } = GateStatus.Pending;
    public int Passed { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public TimeSpan Duration { get; set; }
    public string? TrxPath { get; set; }
    public string? StdOut { get; set; }
    public string? ErrorMessage { get; set; }
    public List<TestOutcome> Outcomes { get; } = new();

    public int Total => Passed + Failed + Skipped;

    public void ComputeStatus()
    {
        if (Total == 0)
        {
            Status = Failed > 0 ? GateStatus.Red : GateStatus.Error;
            return;
        }
        if (Failed == 0)
        {
            Status = GateStatus.Green;
            return;
        }
        var failRate = (double)Failed / Total;
        Status = failRate <= 0.10 ? GateStatus.Yellow : GateStatus.Red;
    }
}
