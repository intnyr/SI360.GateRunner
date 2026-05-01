using System.Text.Json;
using SI360.GateRunner.Models;

namespace SI360.GateRunner.Services;

public interface IReportHistoryAnalyzer
{
    ReportHistoryComparison Compare(RunSummary current, string outputDir);
}

public sealed class ReportHistoryAnalyzer : IReportHistoryAnalyzer
{
    public ReportHistoryComparison Compare(RunSummary current, string outputDir)
    {
        var comparison = new ReportHistoryComparison();
        if (!Directory.Exists(outputDir))
            return comparison;

        var currentName = current.StartedAt.ToString("yyyyMMdd_HHmmss");
        var priorPath = Directory.EnumerateFiles(outputDir, "GateRun_*.json")
            .Where(path => !Path.GetFileNameWithoutExtension(path).EndsWith(currentName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (priorPath is null)
            return comparison;

        comparison.PriorRunFound = true;
        comparison.PriorReportPath = priorPath;

        var prior = LoadPrior(priorPath);
        comparison.PriorStartedAt = prior.StartedAt;

        var currentFailures = CurrentFailures(current)
            .GroupBy(FingerprintKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var priorFailures = prior.Failures
            .GroupBy(FingerprintKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var currentFailure in currentFailures)
        {
            if (priorFailures.ContainsKey(currentFailure.Key))
                comparison.RecurringFailures.Add(currentFailure.Value);
            else
                comparison.NewFailures.Add(currentFailure.Value);
        }

        foreach (var priorFailure in priorFailures)
        {
            if (!currentFailures.ContainsKey(priorFailure.Key))
                comparison.ResolvedFailures.Add(priorFailure.Value);
        }

        return comparison;
    }

    private static IEnumerable<FailureFingerprint> CurrentFailures(RunSummary current) =>
        current.GateResults.SelectMany(g => g.Outcomes
            .Where(o => o.Status == TestStatus.Failed)
            .Select(o => new FailureFingerprint(o.GateName, o.TestName, o.FilePath, o.LineNumber)));

    private static PriorReport LoadPrior(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var prior = new PriorReport();

        if (root.TryGetProperty("startedAt", out var started) && started.TryGetDateTime(out var startedAt))
            prior.StartedAt = startedAt;

        if (!root.TryGetProperty("gates", out var gates) || gates.ValueKind != JsonValueKind.Array)
            return prior;

        foreach (var gate in gates.EnumerateArray())
        {
            var gateName = GetString(gate, "id") ?? GetString(gate, "name") ?? string.Empty;
            if (!gate.TryGetProperty("failures", out var failures) || failures.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var failure in failures.EnumerateArray())
            {
                prior.Failures.Add(new FailureFingerprint(
                    gateName,
                    GetString(failure, "TestName") ?? GetString(failure, "testName") ?? string.Empty,
                    GetString(failure, "FilePath") ?? GetString(failure, "filePath"),
                    GetInt(failure, "LineNumber") ?? GetInt(failure, "lineNumber")));
            }
        }

        return prior;
    }

    private static string FingerprintKey(FailureFingerprint failure) =>
        $"{failure.GateName}|{failure.TestName}|{failure.FilePath}|{failure.LineNumber}";

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static int? GetInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.TryGetInt32(out var value)
            ? value
            : null;

    private sealed class PriorReport
    {
        public DateTime? StartedAt { get; set; }
        public List<FailureFingerprint> Failures { get; } = new();
    }
}
