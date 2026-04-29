namespace SI360.GateRunner.Models;

public enum GateCategory
{
    Build,
    Pipeline,
    Data,
    Foundation,
    Monitoring,
    Coverage,
    Scale,
    Orchestrator,
    Production,
    Safety,
    Scorecard,
    ScenarioWeights,
    Security,
    StagedRollout,
    TestInventory
}

public sealed record GateDefinition(
    string Id,
    string DisplayName,
    string TestClassFilter,
    int ExpectedTestCount,
    GateCategory Category,
    string Notes);
