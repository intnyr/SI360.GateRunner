using System.Globalization;
using System.Text;
using System.Text.Json;
using SI360.GateRunner.Models;

namespace SI360.GateRunner.Services;

public interface IReportWriter
{
    Task<(string MarkdownPath, string JsonPath)> WriteAsync(RunSummary summary, string outputDir);
}

public sealed class ReportWriter : IReportWriter
{
    public const string SchemaVersion = "2.0";
    private readonly IReportHistoryAnalyzer _historyAnalyzer;

    public ReportWriter()
        : this(new ReportHistoryAnalyzer())
    {
    }

    public ReportWriter(IReportHistoryAnalyzer historyAnalyzer)
    {
        _historyAnalyzer = historyAnalyzer;
    }

    public async Task<(string MarkdownPath, string JsonPath)> WriteAsync(RunSummary summary, string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        summary.History = _historyAnalyzer.Compare(summary, outputDir);

        var ts = summary.StartedAt.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var mdPath = Path.Combine(outputDir, $"GateRun_{ts}.md");
        var jsonPath = Path.Combine(outputDir, $"GateRun_{ts}.json");

        await File.WriteAllTextAsync(mdPath, BuildMarkdown(summary), Encoding.UTF8);
        await File.WriteAllTextAsync(jsonPath, BuildJson(summary), Encoding.UTF8);
        return (mdPath, jsonPath);
    }

    private static string BuildMarkdown(RunSummary s)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# SI360 Pre-Deployment Gate Run - {s.StartedAt:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine($"**Duration:** {s.Duration.TotalSeconds:0.0}s  ");
        sb.AppendLine($"**Decision:** **{Label(s.Decision)}**  ");
        if (!string.IsNullOrWhiteSpace(s.DecisionPolicyName))
            sb.AppendLine($"**Decision Policy:** {s.DecisionPolicyName} v{s.DecisionPolicyVersion}  ");
        if (!string.IsNullOrWhiteSpace(s.DecisionRationale))
            sb.AppendLine($"**Decision Rationale:** {Escape(s.DecisionRationale)}  ");
        sb.AppendLine($"**Overall Score:** {s.Scorecard.OverallScore:0.00} ({s.Scorecard.Grade})  ");
        sb.AppendLine($"**Scenario:** {s.Scorecard.ScenarioScore:0.0} / 100 &nbsp;&nbsp; **Probabilistic:** {s.Scorecard.ProbabilisticScore:0.0} / 100");
        sb.AppendLine();

        sb.AppendLine("## Run Environment");
        sb.AppendLine();
        sb.AppendLine("| Field | Value |");
        sb.AppendLine("|-------|-------|");
        sb.AppendLine($"| Tool Version | {Escape(s.Environment.ToolVersion)} |");
        sb.AppendLine($"| .NET SDK | {Escape(s.Environment.DotnetSdkVersion)} |");
        sb.AppendLine($"| Repository | `{Escape(s.Environment.RepositoryPath)}` |");
        sb.AppendLine($"| Branch | {Escape(s.Environment.Branch)} |");
        sb.AppendLine($"| Commit | `{Escape(s.Environment.Commit)}` |");
        sb.AppendLine($"| Artifacts | `{Escape(s.Environment.ArtifactDirectory)}` |");
        sb.AppendLine();

        if (s.GateCatalogWarnings.Count > 0)
        {
            sb.AppendLine("## Gate Catalog Warnings");
            sb.AppendLine();
            sb.AppendLine("| Code | Message |");
            sb.AppendLine("|------|---------|");
            foreach (var w in s.GateCatalogWarnings)
                sb.AppendLine($"| {w.Code} | {Escape(w.Message)} |");
            sb.AppendLine();
        }

        sb.AppendLine("## Run History");
        sb.AppendLine();
        if (s.History.PriorRunFound)
        {
            sb.AppendLine($"Prior report: `{Escape(s.History.PriorReportPath ?? string.Empty)}`");
            sb.AppendLine();
            sb.AppendLine($"- New failures: {s.History.NewFailures.Count}");
            sb.AppendLine($"- Recurring failures: {s.History.RecurringFailures.Count}");
            sb.AppendLine($"- Resolved failures: {s.History.ResolvedFailures.Count}");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("No prior JSON report found for comparison.");
            sb.AppendLine();
        }

        if (s.BuildErrors.Count > 0)
        {
            sb.AppendLine("## Build Errors");
            sb.AppendLine();
            sb.AppendLine("| File | Line | Code | Message |");
            sb.AppendLine("|------|-----:|------|---------|");
            foreach (var e in s.BuildErrors)
                sb.AppendLine($"| `{Escape(e.FilePath)}` | {e.Line} | {e.Code} | {Escape(e.Message)} |");
            sb.AppendLine();
        }

        sb.AppendLine("## Gates");
        sb.AppendLine();
        sb.AppendLine("| # | Gate | Status | Passed | Failed | Skipped | Duration |");
        sb.AppendLine("|---|------|--------|-------:|-------:|--------:|---------:|");
        var i = 1;
        foreach (var g in s.GateResults)
            sb.AppendLine($"| {i++} | {g.Definition.DisplayName} | {g.Status} | {g.Passed} | {g.Failed} | {g.Skipped} | {g.Duration.TotalSeconds:0.0}s |");
        sb.AppendLine();

        sb.AppendLine("## Satisfaction Scorecard");
        sb.AppendLine();
        sb.AppendLine("### Scenarios (weight-based)");
        sb.AppendLine();
        sb.AppendLine("| Scenario | Weight | Result |");
        sb.AppendLine("|----------|-------:|--------|");
        foreach (var sc in s.Scorecard.Scenarios)
            sb.AppendLine($"| {sc.Name} | {sc.Weight} | {(sc.Passed ? "PASS" : "FAIL")} |");
        sb.AppendLine();
        sb.AppendLine("### Probabilistic");
        sb.AppendLine();
        sb.AppendLine("| Suite | Success Rate | Score | P95 (ms) |");
        sb.AppendLine("|-------|-------------:|------:|---------:|");
        foreach (var p in s.Scorecard.Probabilistics)
            sb.AppendLine($"| {p.Name} | {p.SuccessRatePct:0.0}% | {p.Score} | {(p.P95Ms?.ToString("0.0") ?? "-")} |");
        sb.AppendLine();

        var failures = s.GateResults
            .SelectMany(g => g.Outcomes.Where(o => o.Status == TestStatus.Failed))
            .ToList();
        if (failures.Count > 0)
        {
            sb.AppendLine($"## Failure Inventory ({failures.Count})");
            sb.AppendLine();
            var n = 1;
            foreach (var f in failures)
            {
                sb.AppendLine($"### {n++}. `{f.TestName}`");
                sb.AppendLine($"- **Gate:** {f.GateName}");
                sb.AppendLine($"- **Failure Type:** {ClassifyFailure(f)}");
                sb.AppendLine($"- **Error Message:** `{Escape(f.ErrorMessage ?? string.Empty)}`");
                var loc = f.FilePath is null ? "-" : $"`{f.FilePath}:{f.LineNumber}`";
                sb.AppendLine($"- **File/Component:** {loc} -> `{InferComponent(f.FilePath)}`");
                sb.AppendLine();
            }
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("*Generated by SI360.GateRunner.*");
        return sb.ToString();
    }

    private static string BuildJson(RunSummary s)
    {
        var dto = new
        {
            schemaVersion = SchemaVersion,
            startedAt = s.StartedAt,
            durationSeconds = s.Duration.TotalSeconds,
            decision = Label(s.Decision),
            environment = s.Environment,
            decisionPolicy = new
            {
                name = s.DecisionPolicyName,
                version = s.DecisionPolicyVersion,
                rationale = s.DecisionRationale
            },
            gateCatalogWarnings = s.GateCatalogWarnings,
            scorecard = new
            {
                scenarioScore = s.Scorecard.ScenarioScore,
                probabilisticScore = s.Scorecard.ProbabilisticScore,
                overallScore = s.Scorecard.OverallScore,
                grade = s.Scorecard.Grade,
                scenarios = s.Scorecard.Scenarios,
                probabilistics = s.Scorecard.Probabilistics
            },
            history = new
            {
                priorRunFound = s.History.PriorRunFound,
                priorReportPath = s.History.PriorReportPath,
                priorStartedAt = s.History.PriorStartedAt,
                newFailures = s.History.NewFailures,
                recurringFailures = s.History.RecurringFailures,
                resolvedFailures = s.History.ResolvedFailures
            },
            buildErrors = s.BuildErrors,
            gates = s.GateResults.Select(g => new
            {
                id = g.Definition.Id,
                name = g.Definition.DisplayName,
                status = g.Status.ToString(),
                passed = g.Passed,
                failed = g.Failed,
                skipped = g.Skipped,
                durationSeconds = g.Duration.TotalSeconds,
                trxPath = g.TrxPath,
                errorMessage = g.ErrorMessage,
                failures = g.Outcomes.Where(o => o.Status == TestStatus.Failed).Select(o => new
                {
                    o.TestName,
                    o.ErrorMessage,
                    o.FilePath,
                    o.LineNumber,
                    o.StackTrace
                })
            })
        };
        return JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string Label(DeployDecision d) => d switch
    {
        DeployDecision.Go => "GO",
        DeployDecision.Hold => "HOLD",
        _ => "NO-GO"
    };

    private static string Escape(string s) =>
        s.Replace("|", "\\|").Replace("`", "\\`").Replace("\r", " ").Replace("\n", " ");

    private static string ClassifyFailure(TestOutcome o)
    {
        var msg = (o.ErrorMessage ?? string.Empty) + "\n" + (o.StackTrace ?? string.Empty);
        if (msg.Contains("MockException", StringComparison.OrdinalIgnoreCase)) return "Mock";
        if (msg.Contains("Timeout", StringComparison.OrdinalIgnoreCase)) return "Timeout";
        if (msg.Contains("Assert", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("Expected", StringComparison.OrdinalIgnoreCase)) return "Assertion";
        if (msg.Contains("Sql", StringComparison.OrdinalIgnoreCase)) return "Database";
        return "Failure";
    }

    private static string InferComponent(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return "Unknown";
        var normalized = filePath.Replace('\\', '/');
        var marker = "/SI36020WPF/";
        var idx = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return idx >= 0 ? normalized[(idx + marker.Length)..] : Path.GetFileName(filePath);
    }
}
