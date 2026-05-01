namespace SI360.GateRunner.Models;

public enum SyntheticProbeStatus
{
    Passed,
    Failed,
    Skipped,
    Error
}

public sealed record SyntheticProbeResult(
    string Id,
    string Name,
    string Endpoint,
    string ContractVersion,
    SyntheticProbeStatus Status,
    double DurationMs,
    string Diagnostics);
