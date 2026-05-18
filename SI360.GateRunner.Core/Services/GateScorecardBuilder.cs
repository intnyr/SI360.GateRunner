using SI360.GateRunner.Models;

namespace SI360.GateRunner.Services;

public static class GateScorecardBuilder
{
    public static IReadOnlyList<GateScorecardItem> Build(IEnumerable<GateResult>? gateResults)
    {
        var byId = gateResults?
            .GroupBy(g => g.Definition.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, GateResult>(StringComparer.OrdinalIgnoreCase);

        return GateCatalog.All
            .Select(definition =>
            {
                byId.TryGetValue(definition.Id, out var result);
                return From(definition, result);
            })
            .ToList();
    }

    private static GateScorecardItem From(GateDefinition definition, GateResult? result)
    {
        if (result is null)
        {
            return new GateScorecardItem(
                definition.DisplayName,
                definition.Category.ToString(),
                definition.ExpectedTestCount,
                "Unverified",
                "-",
                $"0/{definition.ExpectedTestCount} verified",
                "No gate result was produced for this catalog gate.",
                "Missing run evidence; the Scorecard does not assume a passing grade.",
                string.Empty);
        }

        var score = ComputeVerifiedScore(definition, result);
        var grade = GradeFrom(score, result);
        var expected = Math.Max(definition.ExpectedTestCount, result.Total);
        var trx = result.TrxPath ?? string.Empty;
        var evidence = string.IsNullOrWhiteSpace(trx)
            ? "TRX not recorded."
            : $"TRX: {trx}";

        return new GateScorecardItem(
            definition.DisplayName,
            definition.Category.ToString(),
            definition.ExpectedTestCount,
            result.Status.ToString(),
            grade,
            $"{result.Passed}/{expected} verified ({result.Failed} failed, {result.Skipped} skipped)",
            evidence,
            BuildReason(definition, result, score),
            trx);
    }

    private static double ComputeVerifiedScore(GateDefinition definition, GateResult result)
    {
        var denominator = Math.Max(definition.ExpectedTestCount, result.Total);
        if (denominator <= 0)
            return 0;

        return Math.Round(Math.Clamp((double)result.Passed / denominator * 100, 0, 100), 2);
    }

    private static string GradeFrom(double score, GateResult result)
    {
        if (result.Total == 0)
            return "-";
        if (result.Status is GateStatus.Error or GateStatus.Red)
            return "F";

        return score switch
        {
            >= 95 => "A+",
            >= 90 => "A",
            >= 80 => "B+",
            >= 70 => "B",
            >= 60 => "C",
            _ => "F"
        };
    }

    private static string BuildReason(GateDefinition definition, GateResult result, double score)
    {
        if (result.Total == 0)
        {
            return string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? "No parsed test outcomes were available."
                : result.ErrorMessage;
        }

        var expectedNote = result.Total < definition.ExpectedTestCount
            ? $" Expected {definition.ExpectedTestCount}, parsed {result.Total}; grade is capped by missing evidence."
            : string.Empty;

        return $"{result.Passed} passed, {result.Failed} failed, {result.Skipped} skipped; verified score {score:0.##}%.{expectedNote}";
    }
}
