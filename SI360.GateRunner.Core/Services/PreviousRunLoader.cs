using System.IO;
using System.Text.Json;

namespace SI360.GateRunner.Services;

public sealed record PreviousRun(DateTime StartedAt, double OverallScore, string Grade, string Decision);

public static class PreviousRunLoader
{
    public static string? LastLoadError { get; private set; }

    public static PreviousRun? LoadLatest(string resultsDir)
    {
        LastLoadError = null;
        if (string.IsNullOrWhiteSpace(resultsDir) || !Directory.Exists(resultsDir)) return null;
        var files = Directory.GetFiles(resultsDir, "GateRun_*.json", SearchOption.TopDirectoryOnly);
        if (files.Length == 0) return null;
        Array.Sort(files, (a, b) => string.CompareOrdinal(b, a));
        var skipped = new List<string>();
        foreach (var f in files)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(f));
                var root = doc.RootElement;
                var startedAt = root.GetProperty("startedAt").GetDateTime();
                var overall = root.GetProperty("scorecard").GetProperty("overallScore").GetDouble();
                var grade = root.GetProperty("scorecard").GetProperty("grade").GetString() ?? "?";
                var decision = root.GetProperty("decision").GetString() ?? "NO-GO";
                if (skipped.Count > 0)
                    LastLoadError = $"Skipped {skipped.Count} unreadable report(s): {string.Join(", ", skipped.Select(Path.GetFileName))}";
                return new PreviousRun(startedAt, overall, grade, decision);
            }
            catch (Exception ex)
            {
                skipped.Add($"{f} ({ex.Message})");
            }
        }
        if (skipped.Count > 0)
            LastLoadError = $"All {skipped.Count} prior report(s) unreadable: {string.Join("; ", skipped.Select(s => Path.GetFileName(s)))}";
        return null;
    }
}
