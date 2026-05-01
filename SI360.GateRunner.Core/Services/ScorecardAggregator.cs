using System.Globalization;
using System.Text.RegularExpressions;
using SI360.GateRunner.Models;

namespace SI360.GateRunner.Services;

public sealed partial class ScorecardAggregator
{
    private static readonly Dictionary<string, int> ScenarioWeights = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Happy Path Order"] = 25,
        ["Error Recovery"] = 20,
        ["Security Stress"] = 20,
        ["Multi-Device Sync"] = 15,
        ["Edge Cases"] = 20
    };

    private static readonly string[] ProbabilisticOrder =
    {
        "Success Rate",
        "Error Recovery Rate",
        "State Consistency",
        "Concurrency Safety",
        "Performance Distribution"
    };

    [GeneratedRegex(@"\[GATE\]\s+(?<name>Happy Path Order|Error Recovery|Security Stress|Multi-Device Sync|Edge Cases):\s+(?<result>PASS|FAIL)", RegexOptions.Compiled)]
    private static partial Regex ScenarioRegex();

    [GeneratedRegex(@"\[GATE\]\s+(?<name>Success Rate|Error Recovery Rate|State Consistency|Concurrency Safety|Performance Distribution):\s+(?<rate>[\d.]+)%(?:\s+P95=(?<p95>[\d.]+)ms)?", RegexOptions.Compiled)]
    private static partial Regex ProbabilisticRegex();

    public Scorecard Build(IEnumerable<GateResult> gateResults)
    {
        var card = new Scorecard();
        var orchestrator = gateResults.FirstOrDefault(g =>
            string.Equals(g.Definition.Id, "PreDeploymentGate", StringComparison.OrdinalIgnoreCase));

        if (orchestrator is null)
        {
            foreach (var kv in ScenarioWeights)
                card.Scenarios.Add(new ScenarioPass(kv.Key, kv.Value, false));
            foreach (var n in ProbabilisticOrder)
                card.Probabilistics.Add(new ProbabilisticEntry(n, 0, 0, null, null, null));
            return card;
        }

        var stdOut = orchestrator.StdOut ?? string.Empty;
        foreach (var oc in orchestrator.Outcomes)
            if (!string.IsNullOrEmpty(oc.StdOut)) stdOut += "\n" + oc.StdOut;

        foreach (var kv in ScenarioWeights)
        {
            var passed = false;
            foreach (Match m in ScenarioRegex().Matches(stdOut))
            {
                if (!string.Equals(m.Groups["name"].Value, kv.Key, StringComparison.OrdinalIgnoreCase)) continue;
                passed = string.Equals(m.Groups["result"].Value, "PASS", StringComparison.OrdinalIgnoreCase);
            }
            card.Scenarios.Add(new ScenarioPass(kv.Key, kv.Value, passed));
        }

        foreach (var n in ProbabilisticOrder)
        {
            double rate = 0;
            double? p95 = null;
            foreach (Match m in ProbabilisticRegex().Matches(stdOut))
            {
                if (!string.Equals(m.Groups["name"].Value, n, StringComparison.OrdinalIgnoreCase)) continue;
                double.TryParse(m.Groups["rate"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out rate);
                if (m.Groups["p95"].Success && double.TryParse(m.Groups["p95"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var p))
                    p95 = p;
            }
            card.Probabilistics.Add(new ProbabilisticEntry(n, rate, ScoreProb(rate), null, p95, null));
        }

        card.ScenarioScore = card.Scenarios.Where(s => s.Passed).Sum(s => s.Weight);
        card.ProbabilisticScore = card.Probabilistics.Count == 0 ? 0
            : Math.Round(card.Probabilistics.Average(p => (double)p.Score), 2);
        return card;
    }

    private static int ScoreProb(double ratePct) => ratePct switch
    {
        >= 99 => 100,
        >= 95 => 50,
        _ => 0
    };
}
