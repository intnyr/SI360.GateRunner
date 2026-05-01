using System.Text.Json;
using SI360.GateRunner.Models;
using SI360.GateRunner.Services;

namespace SI360.GateRunner.Tests;

public sealed class DeploymentMetadataValidatorTests
{
    [Fact]
    public void Validate_AcceptsCompleteMetadata()
    {
        var result = new DeploymentMetadataValidator().Validate(ValidMetadata());

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Validate_ReportsMissingRequiredFields()
    {
        var result = new DeploymentMetadataValidator().Validate(new DeploymentMetadata());

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Field == nameof(DeploymentMetadata.SiteId));
        Assert.Contains(result.Issues, i => i.Field == nameof(DeploymentMetadata.Si360SignalRHubUrl));
        Assert.Contains(result.Issues, i => i.Field == nameof(DeploymentMetadata.TerminalIds));
    }

    [Fact]
    public void Validate_ReportsMalformedEndpointsAndIds()
    {
        var metadata = ValidMetadata();
        metadata.SiteId = "bad site!";
        metadata.Si360SignalRHubUrl = "localhost:8080";
        metadata.SyncHealthHubEndpoint = "ftp://health";
        metadata.TerminalIds = new List<string> { "bad id!" };

        var result = new DeploymentMetadataValidator().Validate(metadata);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Code == "SITE_ID_INVALID");
        Assert.Contains(result.Issues, i => i.Field == nameof(DeploymentMetadata.Si360SignalRHubUrl));
        Assert.Contains(result.Issues, i => i.Field == nameof(DeploymentMetadata.SyncHealthHubEndpoint));
        Assert.Contains(result.Issues, i => i.Code == "METADATA_ID_INVALID");
    }

    [Fact]
    public void Validate_RejectsSecretValuesAndSecretLikeFields()
    {
        var metadata = ValidMetadata();
        metadata.SyncHealthHubEndpoint = "https://health.example.test/status?token=super-secret-token";
        metadata.ExtensionData = new Dictionary<string, JsonElement>
        {
            ["apiKey"] = JsonDocument.Parse("\"super-secret-token\"").RootElement.Clone()
        };

        var result = new DeploymentMetadataValidator().Validate(metadata);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Code == "METADATA_SECRET_VALUE_PRESENT");
        Assert.Contains(result.Issues, i => i.Code == "METADATA_SECRET_FIELD_PRESENT");
    }

    [Fact]
    public void LoadAndValidate_ReadsMetadataFile()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "deployment-metadata.json");
        File.WriteAllText(path, JsonSerializer.Serialize(ValidMetadata()));

        var result = new DeploymentMetadataValidator().LoadAndValidate(path);

        Assert.True(result.IsValid);
        Assert.Equal("SITE-001", result.Metadata?.SiteId);
    }

    private static DeploymentMetadata ValidMetadata() => new()
    {
        SchemaVersion = "1.0",
        SiteId = "SITE-001",
        EnvironmentName = "Staging",
        DeploymentVersion = "2026.05.01.1",
        Si360SignalRHubUrl = "https://si360.example.test/hubs/orders",
        Si360ApiKeyPresent = true,
        SyncHealthHubEndpoint = "https://health.example.test/health",
        ThirdPartyKdsEndpoint = "https://kds.example.test/health",
        SiteSqlConnectionReference = "KeyVault:si360-site-sql",
        TerminalIds = new List<string> { "TERM-01" },
        TabletIds = new List<string> { "TAB-01" },
        KdsStationIds = new List<string> { "KDS-STATION-01" },
        KdsDisplayIds = new List<string> { "KDS-DISPLAY-01" }
    };

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gaterunner-metadata-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
