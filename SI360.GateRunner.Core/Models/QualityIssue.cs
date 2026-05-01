namespace SI360.GateRunner.Models;

public enum QualityIssueSeverity
{
    Error,
    Warning
}

public enum QualityIssueSource
{
    Build,
    Gate,
    GateCatalog,
    DeploymentMetadata,
    SyntheticProbe,
    RuntimeReadiness
}

public sealed record QualityIssue(
    string Id,
    QualityIssueSeverity Severity,
    QualityIssueSource Source,
    string SourceLocation,
    string Code,
    string Message,
    double ScoreImpact,
    string DecisionImpact,
    string? ArtifactPath = null)
{
    public bool IsError => Severity == QualityIssueSeverity.Error;
    public bool IsWarning => Severity == QualityIssueSeverity.Warning;
}
