using System.Text.Json;
using System.Text.Json.Serialization;

namespace SI360.GateRunner.Models;

public sealed class DeploymentMetadata
{
    public string SchemaVersion { get; set; } = "1.0";
    public string SiteId { get; set; } = string.Empty;
    public string EnvironmentName { get; set; } = string.Empty;
    public string DeploymentVersion { get; set; } = string.Empty;
    public string Si360SignalRHubUrl { get; set; } = string.Empty;
    public bool Si360ApiKeyPresent { get; set; }
    public string SyncHealthHubEndpoint { get; set; } = string.Empty;
    public string ThirdPartyKdsEndpoint { get; set; } = string.Empty;
    public string SiteSqlConnectionReference { get; set; } = string.Empty;
    public List<string> TerminalIds { get; set; } = new();
    public List<string> TabletIds { get; set; } = new();
    public List<string> KdsStationIds { get; set; } = new();
    public List<string> KdsDisplayIds { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed record DeploymentMetadataIssue(
    string Code,
    string Field,
    string Message,
    string Severity);

public sealed class DeploymentMetadataValidationResult
{
    public DeploymentMetadata? Metadata { get; set; }
    public List<DeploymentMetadataIssue> Issues { get; } = new();
    public bool IsValid => Issues.All(i => !string.Equals(i.Severity, "Error", StringComparison.OrdinalIgnoreCase));
}
