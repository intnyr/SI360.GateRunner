using SI360.GateRunner.Models;

namespace SI360.GateRunner.ViewModels;

public sealed record FailureItemViewModel(
    string GateName,
    string TestName,
    string FailureType,
    string ErrorMessage,
    string? StackTrace,
    string? FilePath,
    int? LineNumber,
    string ComponentHint)
{
    public string Location => FilePath is null ? "—" : $"{System.IO.Path.GetFileName(FilePath)}:{LineNumber?.ToString() ?? "?"}";

    // Lower = more severe (sort ascending).
    public int SeverityRank => FailureType switch
    {
        "Gate composite < 95" => 0,
        "Moq.MockException" => 1,
        "NullReferenceException" => 2,
        "Timeout" => 3,
        "Assertion" => 4,
        "Failure" => 5,
        "Skipped" => 9,
        _ => 6
    };

    public string ToMarkdown()
    {
        var loc = FilePath is null ? "—" : $"`{FilePath}:{LineNumber}`";
        return $"### `{TestName}`\n" +
               $"- **Gate:** {GateName}\n" +
               $"- **Failure Type:** {FailureType}\n" +
               $"- **Error Message:** `{ErrorMessage}`\n" +
               $"- **File/Component:** {loc} → `{ComponentHint}`\n";
    }

    public static FailureItemViewModel From(TestOutcome o)
    {
        var type = ClassifyFailure(o);
        var hint = o.FilePath is null ? o.GateName : TryInferComponent(o.FilePath) ?? o.GateName;
        return new FailureItemViewModel(
            o.GateName,
            o.TestName,
            type,
            o.ErrorMessage ?? "(no message)",
            o.StackTrace,
            o.FilePath,
            o.LineNumber,
            hint);
    }

    private static string ClassifyFailure(TestOutcome o)
    {
        if (o.Status == TestStatus.Skipped) return "Skipped";
        var msg = o.ErrorMessage ?? string.Empty;
        if (msg.Contains("Moq.MockException", StringComparison.OrdinalIgnoreCase)) return "Moq.MockException";
        if (msg.Contains("NullReferenceException", StringComparison.OrdinalIgnoreCase)) return "NullReferenceException";
        if (msg.Contains("Timeout", StringComparison.OrdinalIgnoreCase)) return "Timeout";
        if (msg.Contains("composite", StringComparison.OrdinalIgnoreCase) && msg.Contains("95")) return "Gate composite < 95";
        if (msg.Contains("Assert.", StringComparison.OrdinalIgnoreCase) || msg.Contains("Xunit", StringComparison.OrdinalIgnoreCase))
            return "Assertion";
        return "Failure";
    }

    private static string? TryInferComponent(string filePath)
    {
        var idx = filePath.IndexOf("SI360.", StringComparison.OrdinalIgnoreCase);
        return idx >= 0 ? filePath[idx..].Replace('\\', '/') : null;
    }
}
