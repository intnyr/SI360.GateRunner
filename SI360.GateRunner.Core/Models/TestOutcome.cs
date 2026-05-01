namespace SI360.GateRunner.Models;

public enum TestStatus
{
    Passed,
    Failed,
    Skipped,
    Unknown
}

public sealed record TestOutcome(
    string GateName,
    string TestName,
    TestStatus Status,
    TimeSpan Duration,
    string? ErrorMessage,
    string? StackTrace,
    string? StdOut,
    string? FilePath,
    int? LineNumber);
