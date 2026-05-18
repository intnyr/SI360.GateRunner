namespace SI360.GateRunner.Models;

public sealed record GateScorecardItem(
    string DisplayName,
    string Category,
    int ExpectedTestCount,
    string Status,
    string Grade,
    string ResultSummary,
    string Evidence,
    string Reason,
    string TrxPath);
