using System.Text.Json;
using System.Text.RegularExpressions;
using SI360.GateRunner.Models;

namespace SI360.GateRunner.Services;

public interface IDeploymentMetadataValidator
{
    DeploymentMetadataValidationResult LoadAndValidate(string metadataPath);
    DeploymentMetadataValidationResult Validate(DeploymentMetadata metadata);
}

public sealed partial class DeploymentMetadataValidator : IDeploymentMetadataValidator
{
    private readonly ISecretRedactor _redactor;

    public DeploymentMetadataValidator()
        : this(SecretRedactor.Instance)
    {
    }

    public DeploymentMetadataValidator(ISecretRedactor redactor)
    {
        _redactor = redactor;
    }

    public DeploymentMetadataValidationResult LoadAndValidate(string metadataPath)
    {
        var result = new DeploymentMetadataValidationResult();
        if (string.IsNullOrWhiteSpace(metadataPath))
        {
            result.Issues.Add(Error("METADATA_PATH_REQUIRED", "DeploymentMetadataPath", "Deployment metadata path is required."));
            return result;
        }

        if (!File.Exists(metadataPath))
        {
            result.Issues.Add(Error("METADATA_FILE_NOT_FOUND", "DeploymentMetadataPath", $"Deployment metadata file not found: {metadataPath}"));
            return result;
        }

        try
        {
            var metadata = JsonSerializer.Deserialize<DeploymentMetadata>(
                File.ReadAllText(metadataPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (metadata is null)
            {
                result.Issues.Add(Error("METADATA_EMPTY", "DeploymentMetadata", "Deployment metadata file did not contain a metadata object."));
                return result;
            }

            return Validate(metadata);
        }
        catch (JsonException ex)
        {
            result.Issues.Add(Error("METADATA_JSON_INVALID", "DeploymentMetadata", $"Deployment metadata JSON is invalid: {ex.Message}"));
            return result;
        }
    }

    public DeploymentMetadataValidationResult Validate(DeploymentMetadata metadata)
    {
        var result = new DeploymentMetadataValidationResult { Metadata = metadata };

        Require(result, metadata.SchemaVersion, nameof(metadata.SchemaVersion));
        Require(result, metadata.SiteId, nameof(metadata.SiteId));
        Require(result, metadata.EnvironmentName, nameof(metadata.EnvironmentName));
        Require(result, metadata.DeploymentVersion, nameof(metadata.DeploymentVersion));
        Require(result, metadata.Si360SignalRHubUrl, nameof(metadata.Si360SignalRHubUrl));
        Require(result, metadata.SyncHealthHubEndpoint, nameof(metadata.SyncHealthHubEndpoint));
        Require(result, metadata.SiteSqlConnectionReference, nameof(metadata.SiteSqlConnectionReference));

        if (!string.IsNullOrWhiteSpace(metadata.SiteId) && !SiteIdRegex().IsMatch(metadata.SiteId))
            result.Issues.Add(Error("SITE_ID_INVALID", nameof(metadata.SiteId), "SiteId must be 3-64 characters and contain only letters, numbers, hyphens, or underscores."));

        ValidateUrl(result, metadata.Si360SignalRHubUrl, nameof(metadata.Si360SignalRHubUrl));
        ValidateUrl(result, metadata.SyncHealthHubEndpoint, nameof(metadata.SyncHealthHubEndpoint));
        ValidateOptionalUrl(result, metadata.ThirdPartyKdsEndpoint, nameof(metadata.ThirdPartyKdsEndpoint));

        RequireIds(result, metadata.TerminalIds, nameof(metadata.TerminalIds));
        RequireIds(result, metadata.TabletIds, nameof(metadata.TabletIds));
        RequireIds(result, metadata.KdsStationIds, nameof(metadata.KdsStationIds));
        RequireIds(result, metadata.KdsDisplayIds, nameof(metadata.KdsDisplayIds));

        if (!metadata.Si360ApiKeyPresent)
            result.Issues.Add(Error("SI360_API_KEY_NOT_PRESENT", nameof(metadata.Si360ApiKeyPresent), "Metadata must confirm SI360 API key presence without including the key value."));

        ValidateNoSecret(result, nameof(metadata.SiteSqlConnectionReference), metadata.SiteSqlConnectionReference);
        ValidateNoSecret(result, nameof(metadata.Si360SignalRHubUrl), metadata.Si360SignalRHubUrl);
        ValidateNoSecret(result, nameof(metadata.SyncHealthHubEndpoint), metadata.SyncHealthHubEndpoint);
        ValidateNoSecret(result, nameof(metadata.ThirdPartyKdsEndpoint), metadata.ThirdPartyKdsEndpoint);
        ValidateExtensionData(result, metadata.ExtensionData);

        return result;
    }

    private static void Require(DeploymentMetadataValidationResult result, string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            result.Issues.Add(Error("METADATA_FIELD_REQUIRED", field, $"{field} is required."));
    }

    private static void RequireIds(DeploymentMetadataValidationResult result, IReadOnlyCollection<string> values, string field)
    {
        if (values.Count == 0)
            result.Issues.Add(Error("METADATA_IDS_REQUIRED", field, $"{field} must contain at least one ID."));

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value) || !IdentifierRegex().IsMatch(value))
                result.Issues.Add(Error("METADATA_ID_INVALID", field, $"{field} contains an invalid ID: {value}"));
        }
    }

    private static void ValidateUrl(DeploymentMetadataValidationResult result, string value, string field)
    {
        if (!IsHttpUrl(value))
            result.Issues.Add(Error("METADATA_URL_INVALID", field, $"{field} must be an absolute http or https URL."));
    }

    private static void ValidateOptionalUrl(DeploymentMetadataValidationResult result, string value, string field)
    {
        if (!string.IsNullOrWhiteSpace(value) && !IsHttpUrl(value))
            result.Issues.Add(Error("METADATA_URL_INVALID", field, $"{field} must be an absolute http or https URL when provided."));
    }

    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private void ValidateNoSecret(DeploymentMetadataValidationResult result, string field, string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && _redactor.Redact(value) != value)
            result.Issues.Add(Error("METADATA_SECRET_VALUE_PRESENT", field, $"{field} appears to contain a secret value. Store only presence flags or references."));
    }

    private void ValidateExtensionData(DeploymentMetadataValidationResult result, Dictionary<string, JsonElement>? extensionData)
    {
        if (extensionData is null)
            return;

        foreach (var (key, value) in extensionData)
        {
            if (SecretFieldRegex().IsMatch(key))
                result.Issues.Add(Error("METADATA_SECRET_FIELD_PRESENT", key, $"Unexpected secret-like field '{key}' is not allowed in deployment metadata."));

            if (value.ValueKind == JsonValueKind.String)
                ValidateNoSecret(result, key, value.GetString() ?? string.Empty);
        }
    }

    private static DeploymentMetadataIssue Error(string code, string field, string message) =>
        new(code, field, message, "Error");

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_-]{2,63}$")]
    private static partial Regex SiteIdRegex();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.:-]{0,63}$")]
    private static partial Regex IdentifierRegex();

    [GeneratedRegex(@"(?i)(api[-_ ]?key|apikey|token|password|pwd|secret|client_secret)")]
    private static partial Regex SecretFieldRegex();
}
