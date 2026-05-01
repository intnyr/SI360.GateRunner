using System.Net;
using SI360.GateRunner.Models;
using SI360.GateRunner.Services;

namespace SI360.GateRunner.Tests;

public sealed class SyntheticProbeRunnerTests
{
    [Fact]
    public async Task RunAsync_SkipsWhenProbeModeDisabled()
    {
        var runner = new SyntheticProbeRunner(new HttpClient(new FakeHandler(_ => HttpStatusCode.OK)), SecretRedactor.Instance);

        var results = await runner.RunAsync(
            new RunnerSettings { ProbeMode = "Disabled" },
            new DeploymentMetadataValidationResult { Metadata = ValidMetadata() },
            CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(SyntheticProbeStatus.Skipped, results[0].Status);
    }

    [Fact]
    public async Task RunAsync_ReportsProbeStatusAndRedactsEndpointDiagnostics()
    {
        var runner = new SyntheticProbeRunner(new HttpClient(new FakeHandler(request =>
            request.RequestUri?.Host.Contains("kds", StringComparison.OrdinalIgnoreCase) == true
                ? HttpStatusCode.ServiceUnavailable
                : HttpStatusCode.OK)), SecretRedactor.Instance);

        var metadata = ValidMetadata();
        metadata.SyncHealthHubEndpoint = "https://health.example.test/health?token=super-secret-token";
        var metadataResult = new DeploymentMetadataValidationResult { Metadata = metadata };
        var results = await runner.RunAsync(new RunnerSettings { ProbeMode = "ReadOnly", ProbeTimeoutSeconds = 5 }, metadataResult, CancellationToken.None);

        Assert.Contains(results, r => r.Id == "si360-hub" && r.Status == SyntheticProbeStatus.Passed);
        Assert.Contains(results, r => r.Id == "third-party-kds-health" && r.Status == SyntheticProbeStatus.Failed);
        Assert.DoesNotContain(results, r => r.Endpoint.Contains("super-secret-token", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_SkipsWhenMetadataInvalid()
    {
        var runner = new SyntheticProbeRunner(new HttpClient(new FakeHandler(_ => HttpStatusCode.OK)), SecretRedactor.Instance);
        var metadataResult = new DeploymentMetadataValidationResult();
        metadataResult.Issues.Add(new DeploymentMetadataIssue("BAD", "SiteId", "bad", "Error"));

        var results = await runner.RunAsync(new RunnerSettings { ProbeMode = "ReadOnly" }, metadataResult, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("metadata-invalid", results[0].Id);
        Assert.Equal(SyntheticProbeStatus.Skipped, results[0].Status);
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

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpStatusCode> _statusForRequest;

        public FakeHandler(Func<HttpRequestMessage, HttpStatusCode> statusForRequest)
        {
            _statusForRequest = statusForRequest;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_statusForRequest(request)) { RequestMessage = request });
    }
}
