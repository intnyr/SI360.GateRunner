using System.IO;
using System.Text.Json;

namespace SI360.GateRunner.Services;

public sealed record PreviousRun(DateTime StartedAt, double OverallScore, string Grade, string Decision);

public static class PreviousRunLoader
{
    public static PreviousRun? LoadLatest(string resultsDir)
    {
        if (string.IsNullOrWhiteSpace(resultsDir) || !Directory.Exists(resultsDir)) return null;
        var files = Directory.GetFiles(resultsDir, "GateRun_*.json", SearchOption.TopDirectoryOnly);
        if (files.Length == 0) return null;
        Array.Sort(files, (a, b) => string.CompareOrdinal(b, a));
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
                return new PreviousRun(startedAt, overall, grade, decision);
            }
            catch { }
        }
        return null;
    }
}
