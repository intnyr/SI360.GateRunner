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
    public const string SchemaVersion = "2.1";
    private readonly IReportHistoryAnalyzer _historyAnalyzer;
    private readonly ISecretRedactor _redactor;

    public ReportWriter()
        : this(new ReportHistoryAnalyzer(), SecretRedactor.Instance)
    {
    }

    public ReportWriter(IReportHistoryAnalyzer historyAnalyzer)
        : this(historyAnalyzer, SecretRedactor.Instance)
    {
    }

    public ReportWriter(IReportHistoryAnalyzer historyAnalyzer, ISecretRedactor redactor)
    {
        _historyAnalyzer = historyAnalyzer;
        _redactor = redactor;
    }

    public async Task<(string MarkdownPath, string JsonPath)> WriteAsync(RunSummary summary, string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        summary.History = _historyAnalyzer.Compare(summary, outputDir);

        var startedAtUtc = summary.StartedAt.Kind == DateTimeKind.Utc ? summary.StartedAt : summary.StartedAt.ToUniversalTime();
        var ts = startedAtUtc.ToString("yyyyMMdd_HHmmss'Z'", CultureInfo.InvariantCulture);
        var mdPath = Path.Combine(outputDir, $"GateRun_{ts}.md");
        var jsonPath = Path.Combine(outputDir, $"GateRun_{ts}.json");

        await File.WriteAllTextAsync(mdPath, BuildMarkdown(summary, _redactor), Encoding.UTF8);
        await File.WriteAllTextAsync(jsonPath, BuildJson(summary, _redactor), Encoding.UTF8);
        return (mdPath, jsonPath);
    }

    private static string BuildMarkdown(RunSummary s, ISecretRedactor redactor)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# SI360 Pre-Deployment Gate Run - {s.StartedAt.ToUniversalTime():yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();
        sb.AppendLine($"**Duration:** {s.Duration.TotalSeconds:0.0}s  ");
        sb.AppendLine($"**Decision:** **{Label(s.Decision)}**  ");
        if (!string.IsNullOrWhiteSpace(s.DecisionPolicyName))
            sb.AppendLine($"**Decision Policy:** {Escape(s.DecisionPolicyName, redactor)} v{Escape(s.DecisionPolicyVersion, redactor)}  ");
        if (!string.IsNullOrWhiteSpace(s.DecisionRationale))
            sb.AppendLine($"**Decision Rationale:** {Escape(s.DecisionRationale, redactor)}  ");
        sb.AppendLine($"**Overall Score:** {s.Scorecard.OverallScore:0.00} ({s.Scorecard.Grade})  ");
        sb.AppendLine($"**Scenario:** {s.Scorecard.ScenarioScore:0.0} / 100 &nbsp;&nbsp; **Probabilistic:** {s.Scorecard.ProbabilisticScore:0.0} / 100");
        sb.AppendLine();

        sb.AppendLine("## Run Environment");
        sb.AppendLine();
        sb.AppendLine("| Field | Value |");
        sb.AppendLine("|-------|-------|");
        sb.AppendLine($"| Tool Version | {Escape(s.Environment.ToolVersion, redactor)} |");
        sb.AppendLine($"| .NET SDK | {Escape(s.Environment.DotnetSdkVersion, redactor)} |");
        sb.AppendLine($"| Local UTC Offset | {Escape(s.Environment.LocalUtcOffset, redactor)} |");
        sb.AppendLine($"| Repository | `{Escape(s.Environment.RepositoryPath, redactor)}` |");
        sb.AppendLine($"| Branch | {Escape(s.Environment.Branch, redactor)} |");
        sb.AppendLine($"| Commit | `{Escape(s.Environment.Commit, redactor)}` |");
        sb.AppendLine($"| Artifacts | `{Escape(s.Environment.ArtifactDirectory, redactor)}` |");
        sb.AppendLine();

        if (s.GateCatalogWarnings.Count > 0)
        {
            sb.AppendLine("## Gate Catalog Warnings");
            sb.AppendLine();
            sb.AppendLine("| Code | Message |");
            sb.AppendLine("|------|---------|");
            foreach (var w in s.GateCatalogWarnings)
                sb.AppendLine($"| {Escape(w.Code, redactor)} | {Escape(w.Message, redactor)} |");
            sb.AppendLine();
        }

        sb.AppendLine("## Runtime Readiness");
        sb.AppendLine();
        sb.AppendLine($"**Decision:** {s.RuntimeReadiness}  ");
        sb.AppendLine($"**Rationale:** {Escape(s.RuntimeReadinessRationale, redactor)}  ");
        sb.AppendLine($"**Metadata Valid:** {s.DeploymentMetadata.IsValid}  ");
        sb.AppendLine($"**Probe Count:** {s.SyntheticProbes.Count}  ");
        sb.AppendLine();

        if (s.DeploymentMetadata.Issues.Count > 0)
        {
            sb.AppendLine("### Metadata Issues");
            sb.AppendLine();
            sb.AppendLine("| Severity | Code | Field | Message |");
            sb.AppendLine("|----------|------|-------|---------|");
            foreach (var issue in s.DeploymentMetadata.Issues)
                sb.AppendLine($"| {Escape(issue.Severity, redactor)} | {Escape(issue.Code, redactor)} | {Escape(issue.Field, redactor)} | {Escape(issue.Message, redactor)} |");
            sb.AppendLine();
        }

        if (s.SyntheticProbes.Count > 0)
        {
            sb.AppendLine("### Synthetic Probes");
            sb.AppendLine();
            sb.AppendLine("| Probe | Status | Duration | Endpoint | Diagnostics |");
            sb.AppendLine("|-------|--------|---------:|----------|-------------|");
            foreach (var probe in s.SyntheticProbes)
                sb.AppendLine($"| {Escape(probe.Name, redactor)} | {probe.Status} | {probe.DurationMs:0.0}ms | `{Escape(probe.Endpoint, redactor)}` | {Escape(probe.Diagnostics, redactor)} |");
            sb.AppendLine();
        }

        sb.AppendLine("## Run History");
        sb.AppendLine();
        if (s.History.PriorRunFound)
        {
            sb.AppendLine($"Prior report: `{Escape(s.History.PriorReportPath ?? string.Empty, redactor)}`");
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
                sb.AppendLine($"| `{Escape(e.FilePath, redactor)}` | {e.Line} | {Escape(e.Code, redactor)} | {Escape(e.Message, redactor)} |");
            sb.AppendLine();
        }

        sb.AppendLine("## Gates");
        sb.AppendLine();
        sb.AppendLine("| # | Gate | Status | Passed | Failed | Skipped | Duration |");
        sb.AppendLine("|---|------|--------|-------:|-------:|--------:|---------:|");
        var i = 1;
        foreach (var g in s.GateResults)
            sb.AppendLine($"| {i++} | {Escape(g.Definition.DisplayName, redactor)} | {g.Status} | {g.Passed} | {g.Failed} | {g.Skipped} | {g.Duration.TotalSeconds:0.0}s |");
        sb.AppendLine();

        sb.AppendLine("## Satisfaction Scorecard");
        sb.AppendLine();
        sb.AppendLine("### Scenarios (weight-based)");
        sb.AppendLine();
        sb.AppendLine("| Scenario | Weight | Result |");
        sb.AppendLine("|----------|-------:|--------|");
        foreach (var sc in s.Scorecard.Scenarios)
            sb.AppendLine($"| {Escape(sc.Name, redactor)} | {sc.Weight} | {(sc.Passed ? "PASS" : "FAIL")} |");
        sb.AppendLine();
        sb.AppendLine("### Probabilistic");
        sb.AppendLine();
        sb.AppendLine("| Suite | Success Rate | Score | P95 (ms) |");
        sb.AppendLine("|-------|-------------:|------:|---------:|");
        foreach (var p in s.Scorecard.Probabilistics)
            sb.AppendLine($"| {Escape(p.Name, redactor)} | {p.SuccessRatePct:0.0}% | {p.Score} | {(p.P95Ms?.ToString("0.0") ?? "-")} |");
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
                sb.AppendLine($"### {n++}. `{Escape(f.TestName, redactor)}`");
                sb.AppendLine($"- **Gate:** {Escape(f.GateName, redactor)}");
                sb.AppendLine($"- **Failure Type:** {ClassifyFailure(f)}");
                sb.AppendLine($"- **Error Message:** `{Escape(f.ErrorMessage ?? string.Empty, redactor)}`");
                var loc = f.FilePath is null ? "-" : $"`{Escape(f.FilePath, redactor)}:{f.LineNumber}`";
                sb.AppendLine($"- **File/Component:** {loc} -> `{Escape(InferComponent(f.FilePath), redactor)}`");
                sb.AppendLine();
            }
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("*Generated by SI360.GateRunner.*");
        return sb.ToString();
    }

    private static string BuildJson(RunSummary s, ISecretRedactor redactor)
    {
        var dto = new
        {
            schemaVersion = SchemaVersion,
            startedAt = s.StartedAt,
            durationSeconds = s.Duration.TotalSeconds,
            decision = Label(s.Decision),
            environment = new
            {
                ToolVersion = Safe(s.Environment.ToolVersion, redactor),
                MachineName = Safe(s.Environment.MachineName, redactor),
                OSVersion = Safe(s.Environment.OSVersion, redactor),
                RuntimeVersion = Safe(s.Environment.RuntimeVersion, redactor),
                LocalUtcOffset = Safe(s.Environment.LocalUtcOffset, redactor),
                DotnetSdkVersion = Safe(s.Environment.DotnetSdkVersion, redactor),
                RepositoryPath = Safe(s.Environment.RepositoryPath, redactor),
                Branch = Safe(s.Environment.Branch, redactor),
                Commit = Safe(s.Environment.Commit, redactor),
                ArtifactDirectory = Safe(s.Environment.ArtifactDirectory, redactor),
                Configuration = new
                {
                    BuildConfiguration = Safe(s.Environment.Configuration.BuildConfiguration, redactor),
                    DeploymentMetadataPath = Safe(s.Environment.Configuration.DeploymentMetadataPath, redactor),
                    ProbeMode = Safe(s.Environment.Configuration.ProbeMode, redactor),
                    s.Environment.Configuration.ProbeTimeoutSeconds,
                    s.Environment.Configuration.ReportRetentionDays,
                    SupportBundleOutputPath = Safe(s.Environment.Configuration.SupportBundleOutputPath, redactor)
                },
                Commands = s.Environment.Commands.Select(c => new
                {
                    Name = Safe(c.Name, redactor),
                    CommandLine = Safe(c.CommandLine, redactor),
                    WorkingDirectory = Safe(c.WorkingDirectory, redactor),
                    TimeoutSeconds = c.TimeoutSeconds,
                    ArtifactDirectory = Safe(c.ArtifactDirectory, redactor),
                    ArtifactName = Safe(c.ArtifactName, redactor)
                })
            },
            decisionPolicy = new
            {
                name = Safe(s.DecisionPolicyName, redactor),
                version = Safe(s.DecisionPolicyVersion, redactor),
                rationale = Safe(s.DecisionRationale, redactor)
            },
            runtimeReadiness = new
            {
                decision = s.RuntimeReadiness.ToString(),
                rationale = Safe(s.RuntimeReadinessRationale, redactor)
            },
            healthContracts = new
            {
                si360HealthApi = Safe(s.HealthContracts.Si360HealthApi, redactor),
                syncHealthHub = Safe(s.HealthContracts.SyncHealthHub, redactor),
                thirdPartyKds = Safe(s.HealthContracts.ThirdPartyKds, redactor)
            },
            deploymentMetadata = new
            {
                configured = s.DeploymentMetadata.Metadata is not null,
                valid = s.DeploymentMetadata.IsValid,
                metadata = s.DeploymentMetadata.Metadata is null ? null : new
                {
                    SchemaVersion = Safe(s.DeploymentMetadata.Metadata.SchemaVersion, redactor),
                    SiteId = Safe(s.DeploymentMetadata.Metadata.SiteId, redactor),
                    EnvironmentName = Safe(s.DeploymentMetadata.Metadata.EnvironmentName, redactor),
                    DeploymentVersion = Safe(s.DeploymentMetadata.Metadata.DeploymentVersion, redactor),
                    Si360SignalRHubUrl = Safe(s.DeploymentMetadata.Metadata.Si360SignalRHubUrl, redactor),
                    s.DeploymentMetadata.Metadata.Si360ApiKeyPresent,
                    SyncHealthHubEndpoint = Safe(s.DeploymentMetadata.Metadata.SyncHealthHubEndpoint, redactor),
                    ThirdPartyKdsEndpoint = Safe(s.DeploymentMetadata.Metadata.ThirdPartyKdsEndpoint, redactor),
                    SiteSqlConnectionReference = Safe(s.DeploymentMetadata.Metadata.SiteSqlConnectionReference, redactor),
                    TerminalIds = s.DeploymentMetadata.Metadata.TerminalIds.Select(v => Safe(v, redactor)),
                    TabletIds = s.DeploymentMetadata.Metadata.TabletIds.Select(v => Safe(v, redactor)),
                    KdsStationIds = s.DeploymentMetadata.Metadata.KdsStationIds.Select(v => Safe(v, redactor)),
                    KdsDisplayIds = s.DeploymentMetadata.Metadata.KdsDisplayIds.Select(v => Safe(v, redactor))
                },
                issues = s.DeploymentMetadata.Issues.Select(i => new
                {
                    code = Safe(i.Code, redactor),
                    field = Safe(i.Field, redactor),
                    message = Safe(i.Message, redactor),
                    severity = Safe(i.Severity, redactor)
                })
            },
            syntheticProbes = s.SyntheticProbes.Select(p => new
            {
                id = Safe(p.Id, redactor),
                name = Safe(p.Name, redactor),
                endpoint = Safe(p.Endpoint, redactor),
                contractVersion = Safe(p.ContractVersion, redactor),
                status = p.Status.ToString(),
                durationMs = p.DurationMs,
                diagnostics = Safe(p.Diagnostics, redactor)
            }),
            gateCatalogWarnings = s.GateCatalogWarnings.Select(w => new
            {
                Code = Safe(w.Code, redactor),
                Message = Safe(w.Message, redactor)
            }),
            scorecard = new
            {
                scenarioScore = s.Scorecard.ScenarioScore,
                probabilisticScore = s.Scorecard.ProbabilisticScore,
                overallScore = s.Scorecard.OverallScore,
                grade = s.Scorecard.Grade,
                scenarios = s.Scorecard.Scenarios.Select(sc => new
                {
                    Name = Safe(sc.Name, redactor),
                    sc.Weight,
                    sc.Passed
                }),
                probabilistics = s.Scorecard.Probabilistics.Select(p => new
                {
                    Name = Safe(p.Name, redactor),
                    p.SuccessRatePct,
                    p.Score,
                    p.P50Ms,
                    p.P95Ms,
                    p.P99Ms
                })
            },
            history = new
            {
                priorRunFound = s.History.PriorRunFound,
                priorReportPath = Safe(s.History.PriorReportPath, redactor),
                priorStartedAt = s.History.PriorStartedAt,
                newFailures = RedactFailures(s.History.NewFailures, redactor),
                recurringFailures = RedactFailures(s.History.RecurringFailures, redactor),
                resolvedFailures = RedactFailures(s.History.ResolvedFailures, redactor)
            },
            buildErrors = s.BuildErrors.Select(e => new
            {
                FilePath = Safe(e.FilePath, redactor),
                Line = e.Line,
                Column = e.Column,
                Code = Safe(e.Code, redactor),
                Message = Safe(e.Message, redactor)
            }),
            gates = s.GateResults.Select(g => new
            {
                id = Safe(g.Definition.Id, redactor),
                name = Safe(g.Definition.DisplayName, redactor),
                status = g.Status.ToString(),
                passed = g.Passed,
                failed = g.Failed,
                skipped = g.Skipped,
                durationSeconds = g.Duration.TotalSeconds,
                trxPath = Safe(g.TrxPath, redactor),
                errorMessage = Safe(g.ErrorMessage, redactor),
                failures = g.Outcomes.Where(o => o.Status == TestStatus.Failed).Select(o => new
                {
                    TestName = Safe(o.TestName, redactor),
                    ErrorMessage = Safe(o.ErrorMessage, redactor),
                    FilePath = Safe(o.FilePath, redactor),
                    o.LineNumber,
                    StackTrace = Safe(o.StackTrace, redactor)
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

    private static string Safe(string? s, ISecretRedactor redactor) => redactor.Redact(s);

    private static string Escape(string? s, ISecretRedactor redactor) =>
        Safe(s, redactor).Replace("|", "\\|").Replace("`", "\\`").Replace("\r", " ").Replace("\n", " ");

    private static IEnumerable<object> RedactFailures(IEnumerable<FailureFingerprint> failures, ISecretRedactor redactor) =>
        failures.Select(f => new
        {
            GateName = Safe(f.GateName, redactor),
            TestName = Safe(f.TestName, redactor),
            FilePath = Safe(f.FilePath, redactor),
            f.LineNumber
        });

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
