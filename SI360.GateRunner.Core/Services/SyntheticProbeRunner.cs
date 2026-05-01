using System.Diagnostics;
using SI360.GateRunner.Models;

namespace SI360.GateRunner.Services;

public interface ISyntheticProbeRunner
{
    Task<IReadOnlyList<SyntheticProbeResult>> RunAsync(
        RunnerSettings settings,
        DeploymentMetadataValidationResult metadataResult,
        CancellationToken cancellationToken);
}

public sealed class SyntheticProbeRunner : ISyntheticProbeRunner
{
    private const string ContractVersion = "phase-1-readonly";
    private readonly HttpClient _httpClient;
    private readonly ISecretRedactor _redactor;

    public SyntheticProbeRunner()
        : this(new HttpClient(), SecretRedactor.Instance)
    {
    }

    public SyntheticProbeRunner(HttpClient httpClient, ISecretRedactor redactor)
    {
        _httpClient = httpClient;
        _redactor = redactor;
    }

    public async Task<IReadOnlyList<SyntheticProbeResult>> RunAsync(
        RunnerSettings settings,
        DeploymentMetadataValidationResult metadataResult,
        CancellationToken cancellationToken)
    {
        if (string.Equals(settings.ProbeMode, "Disabled", StringComparison.OrdinalIgnoreCase))
            return new[] { Skipped("probes-disabled", "Synthetic probes disabled", string.Empty, "ProbeMode is Disabled.") };

        if (!metadataResult.IsValid || metadataResult.Metadata is null)
            return new[] { Skipped("metadata-invalid", "Deployment metadata invalid", string.Empty, "Valid deployment metadata is required before probes can run.") };

        var results = new List<SyntheticProbeResult>();
        if (string.Equals(settings.ProbeMode, "Active", StringComparison.OrdinalIgnoreCase))
            results.Add(Skipped(
                "active-mode-not-implemented",
                "Active probe mode requested",
                string.Empty,
                "ProbeMode is Active, but mutating probes are not implemented. Falling back to read-only checks."));

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, settings.ProbeTimeoutSeconds)));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var metadata = metadataResult.Metadata;
        var probes = new[]
        {
            ("si360-hub", "SI360 Hub reachable", metadata.Si360SignalRHubUrl),
            ("si360-health", "SI360 health summary", Combine(metadata.Si360SignalRHubUrl, "/health")),
            ("tablet-registry", "Tablet registry read model", Combine(metadata.Si360SignalRHubUrl, "/health/tablets")),
            ("event-ledger", "Event ledger endpoint", Combine(metadata.Si360SignalRHubUrl, "/health/events")),
            ("kitchen-outbox", "Kitchen outbox endpoint", Combine(metadata.Si360SignalRHubUrl, "/health/kitchen-outbox")),
            ("synchealthhub-ingestion", "SyncHealthHub ingestion endpoint", metadata.SyncHealthHubEndpoint),
            ("third-party-kds-health", "Third-party KDS health endpoint", metadata.ThirdPartyKdsEndpoint),
            ("kds-rendered-confirmation", "KDS rendered-confirmation capability", Combine(metadata.ThirdPartyKdsEndpoint, "/health/rendered-confirmation")),
            ("alert-provider-config", "Alert provider configuration presence", Combine(metadata.SyncHealthHubEndpoint, "/health/alert-provider"))
        };

        foreach (var (id, name, endpoint) in probes)
            results.Add(await ProbeAsync(id, name, endpoint, linkedCts.Token).ConfigureAwait(false));

        return results;
    }

    private async Task<SyntheticProbeResult> ProbeAsync(string id, string name, string endpoint, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return Skipped(id, name, endpoint, "Endpoint not configured.");

        var sw = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.TryAddWithoutValidation("X-GateRunner-Probe", ContractVersion);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            sw.Stop();
            var status = response.IsSuccessStatusCode ? SyntheticProbeStatus.Passed : SyntheticProbeStatus.Failed;
            return new SyntheticProbeResult(id, name, _redactor.Redact(endpoint), ContractVersion, status, sw.Elapsed.TotalMilliseconds, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return new SyntheticProbeResult(id, name, _redactor.Redact(endpoint), ContractVersion, SyntheticProbeStatus.Error, sw.Elapsed.TotalMilliseconds, "Probe timed out or was canceled.");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new SyntheticProbeResult(id, name, _redactor.Redact(endpoint), ContractVersion, SyntheticProbeStatus.Error, sw.Elapsed.TotalMilliseconds, _redactor.Redact(ex.Message));
        }
    }

    private static SyntheticProbeResult Skipped(string id, string name, string endpoint, string diagnostics) =>
        new(id, name, endpoint, ContractVersion, SyntheticProbeStatus.Skipped, 0, diagnostics);

    private static string Combine(string baseUrl, string path)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return string.Empty;
        return new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), path.TrimStart('/')).ToString();
    }
}
