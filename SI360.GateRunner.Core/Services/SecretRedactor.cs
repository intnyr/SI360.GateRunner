using System.Text.RegularExpressions;

namespace SI360.GateRunner.Services;

public interface ISecretRedactor
{
    string Redact(string? value);
}

public sealed partial class SecretRedactor : ISecretRedactor
{
    public const string RedactedValue = "[REDACTED]";
    public static readonly SecretRedactor Instance = new();

    public string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var redacted = BearerTokenRegex().Replace(value, $"$1{RedactedValue}");
        redacted = CommandLineSecretRegex().Replace(redacted, $"$1{RedactedValue}");
        redacted = KeyValueSecretRegex().Replace(redacted, $"$1$2{RedactedValue}");
        redacted = QueryStringSecretRegex().Replace(redacted, $"$1{RedactedValue}");
        redacted = ConnectionStringPasswordRegex().Replace(redacted, $"$1{RedactedValue}");
        return redacted;
    }

    [GeneratedRegex(@"(?i)(Bearer\s+)[A-Za-z0-9._~+/=-]+")]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex(@"(?i)(--(?:api[-_]?key|token|access-token|password|pwd|secret|client-secret)\s+)(?:""[^""]+""|'[^']+'|[^\s;&]+)")]
    private static partial Regex CommandLineSecretRegex();

    [GeneratedRegex(@"(?i)\b(api[-_ ]?key|apikey|x-api-key|token|access_token|password|pwd|secret|client_secret)(\s*[=:]\s*)(""[^""]+""|'[^']+'|[^\s;&]+)")]
    private static partial Regex KeyValueSecretRegex();

    [GeneratedRegex(@"(?i)([?&](?:api[-_]?key|apikey|token|access_token|password|pwd|secret|client_secret)=)([^&#\s]+)")]
    private static partial Regex QueryStringSecretRegex();

    [GeneratedRegex(@"(?i)\b(Password|Pwd)(\s*=\s*)[^;\s]+")]
    private static partial Regex ConnectionStringPasswordRegex();
}
