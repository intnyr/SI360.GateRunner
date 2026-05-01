using System.Text.Json;
using SI360.GateRunner.Models;
using SI360.GateRunner.Services;

namespace SI360.GateRunner.Tests;

public sealed class ReportWriterTests
{
    [Fact]
    public async Task WriteAsync_IncludesDecisionPolicyAndCatalogWarnings()
    {
        using var dir = new TempDirectory();
        var summary = new RunSummary
        {
            StartedAt = new DateTime(2026, 4, 30, 1, 2, 3, DateTimeKind.Utc),
            Duration = TimeSpan.FromSeconds(12),
            Decision = DeployDecision.Hold,
            DecisionPolicyName = "AllGatesAndScorecard",
            DecisionPolicyVersion = "2026.04.30",
            DecisionRationale = "HOLD: test rationale.",
            Scorecard = new Scorecard
            {
                ScenarioScore = 100,
                ProbabilisticScore = 90
            }
        };
        summary.GateCatalogWarnings.Add(new GateCatalogWarning("GATE_TEST_COUNT_DRIFT", "Count drift."));

        var writer = new ReportWriter();
        var (markdownPath, jsonPath) = await writer.WriteAsync(summary, dir.Path);

        Assert.Contains("20260430_010203Z", Path.GetFileName(jsonPath));
        var markdown = await File.ReadAllTextAsync(markdownPath);
        Assert.Contains("Decision Policy", markdown);
        Assert.Contains("Gate Catalog Warnings", markdown);

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath));
        var root = doc.RootElement;
        Assert.Equal(ReportWriter.SchemaVersion, root.GetProperty("schemaVersion").GetString());
        Assert.Equal("HOLD", root.GetProperty("decision").GetString());
        Assert.Equal("AllGatesAndScorecard", root.GetProperty("decisionPolicy").GetProperty("name").GetString());
        Assert.Equal("GATE_TEST_COUNT_DRIFT", root.GetProperty("gateCatalogWarnings")[0].GetProperty("Code").GetString());
        Assert.True(root.GetProperty("environment").TryGetProperty("RuntimeVersion", out _));
        Assert.True(root.GetProperty("history").TryGetProperty("priorRunFound", out _));
    }

    [Fact]
    public async Task WriteAsync_ComparesNewRecurringAndResolvedFailures()
    {
        using var dir = new TempDirectory();
        var priorJson = """
        {
          "startedAt": "2026-04-29T01:02:03Z",
          "gates": [
            {
              "id": "BuildGate",
              "failures": [
                { "TestName": "RecurringFailure", "FilePath": "Recurring.cs", "LineNumber": 10 },
                { "TestName": "ResolvedFailure", "FilePath": "Resolved.cs", "LineNumber": 20 }
              ]
            }
          ]
        }
        """;
        await File.WriteAllTextAsync(Path.Combine(dir.Path, "GateRun_20260429_010203.json"), priorJson);

        var gate = new GateDefinition("BuildGate", "Build Gate", "BuildGateTests", 2, GateCategory.Build, "test");
        var summary = new RunSummary { StartedAt = new DateTime(2026, 4, 30, 1, 2, 3, DateTimeKind.Utc) };
        var result = new GateResult { Definition = gate, Failed = 2 };
        result.Outcomes.Add(new TestOutcome("BuildGate", "RecurringFailure", TestStatus.Failed, TimeSpan.Zero, "failed", null, null, "Recurring.cs", 10));
        result.Outcomes.Add(new TestOutcome("BuildGate", "NewFailure", TestStatus.Failed, TimeSpan.Zero, "failed", null, null, "New.cs", 30));
        summary.GateResults.Add(result);

        var writer = new ReportWriter();
        var (_, jsonPath) = await writer.WriteAsync(summary, dir.Path);

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath));
        var history = doc.RootElement.GetProperty("history");
        Assert.True(history.GetProperty("priorRunFound").GetBoolean());
        Assert.Single(history.GetProperty("newFailures").EnumerateArray());
        Assert.Single(history.GetProperty("recurringFailures").EnumerateArray());
        Assert.Single(history.GetProperty("resolvedFailures").EnumerateArray());
    }

    [Fact]
    public async Task WriteAsync_RedactsSecretsFromMarkdownAndJson()
    {
        using var dir = new TempDirectory();
        var summary = new RunSummary
        {
            StartedAt = new DateTime(2026, 4, 30, 1, 2, 3, DateTimeKind.Utc),
            DecisionRationale = "token=super-secret-token",
            Environment =
            {
                RepositoryPath = @"D:\Repo?token=super-secret-token"
            }
        };
        summary.Environment.Commands.Add(new ProcessCommandSnapshot(
            "restore",
            "dotnet restore --api-key super-secret-token",
            Environment.CurrentDirectory,
            60,
            dir.Path,
            "restore"));
        summary.BuildErrors.Add(new BuildError("Build.cs", 1, 2, "CS0001", "password=super-secret-token"));

        var gate = new GateDefinition("SecurityGate", "Security Gate", "SecurityTests", 1, GateCategory.Security, "test");
        var result = new GateResult
        {
            Definition = gate,
            Failed = 1,
            Status = GateStatus.Red,
            ErrorMessage = "Bearer super-secret-token"
        };
        result.Outcomes.Add(new TestOutcome(
            "Security Gate",
            "RejectsSecrets",
            TestStatus.Failed,
            TimeSpan.Zero,
            "api_key=super-secret-token",
            "client_secret=super-secret-token",
            null,
            "Security.cs",
            10));
        summary.GateResults.Add(result);

        var writer = new ReportWriter();
        var (markdownPath, jsonPath) = await writer.WriteAsync(summary, dir.Path);

        var markdown = await File.ReadAllTextAsync(markdownPath);
        var json = await File.ReadAllTextAsync(jsonPath);
        Assert.DoesNotContain("super-secret-token", markdown);
        Assert.DoesNotContain("super-secret-token", json);
        Assert.Contains(SecretRedactor.RedactedValue, markdown);
        Assert.Contains(SecretRedactor.RedactedValue, json);
    }

    [Fact]
    public async Task WriteAsync_IncludesRuntimeReadinessMetadataAndProbes()
    {
        using var dir = new TempDirectory();
        var summary = new RunSummary
        {
            StartedAt = new DateTime(2026, 4, 30, 1, 2, 3, DateTimeKind.Utc),
            RuntimeReadiness = RuntimeReadinessDecision.Ready,
            RuntimeReadinessRationale = "Metadata valid and probes passed.",
            DeploymentMetadata = new DeploymentMetadataValidationResult
            {
                Metadata = new DeploymentMetadata
                {
                    SiteId = "SITE-001",
                    EnvironmentName = "Staging",
                    DeploymentVersion = "2026.05.01.1",
                    Si360SignalRHubUrl = "https://si360.example.test/hubs/orders",
                    Si360ApiKeyPresent = true,
                    SyncHealthHubEndpoint = "https://health.example.test/health",
                    ThirdPartyKdsEndpoint = "https://kds.example.test/health",
                    SiteSqlConnectionReference = "KeyVault:si360-site-sql",
                    TerminalIds = new List<string> { "TERM-01" },
                    TabletIds = new List<string> { "TAB-01" },
                    KdsStationIds = new List<string> { "KDS-STATION-01" },
                    KdsDisplayIds = new List<string> { "KDS-DISPLAY-01" }
                }
            }
        };
        summary.SyntheticProbes.Add(new SyntheticProbeResult(
            "si360-hub",
            "SI360 Hub reachable",
            "https://si360.example.test/hubs/orders",
            "phase-1-readonly",
            SyntheticProbeStatus.Passed,
            12.3,
            "HTTP 200 OK"));

        var writer = new ReportWriter();
        var (markdownPath, jsonPath) = await writer.WriteAsync(summary, dir.Path);

        var markdown = await File.ReadAllTextAsync(markdownPath);
        Assert.Contains("Runtime Readiness", markdown);
        Assert.Contains("Synthetic Probes", markdown);

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath));
        var root = doc.RootElement;
        Assert.Equal(ReportWriter.SchemaVersion, root.GetProperty("schemaVersion").GetString());
        Assert.Equal("Ready", root.GetProperty("runtimeReadiness").GetProperty("decision").GetString());
        Assert.Equal("SITE-001", root.GetProperty("deploymentMetadata").GetProperty("metadata").GetProperty("SiteId").GetString());
        Assert.Single(root.GetProperty("syntheticProbes").EnumerateArray());
        Assert.True(root.GetProperty("healthContracts").TryGetProperty("syncHealthHub", out _));
    }

    [Fact]
    public async Task WriteAsync_IncludesQualityIssuesAndGradingImpacts()
    {
        using var dir = new TempDirectory();
        var summary = new RunSummary
        {
            StartedAt = new DateTime(2026, 4, 30, 1, 2, 3, DateTimeKind.Utc),
            Decision = DeployDecision.Hold,
            DecisionPolicyName = "AllGatesAndScorecard",
            DecisionPolicyVersion = "2026.05.01",
            DecisionRationale = "HOLD: warning issue.",
            Scorecard = new Scorecard
            {
                ScenarioScore = 100,
                ProbabilisticScore = 100,
                QualityPenalty = 2
            }
        };
        var issue = new QualityIssue(
            "build:warning:CS0618",
            QualityIssueSeverity.Warning,
            QualityIssueSource.Build,
            "Build.cs:12",
            "CS0618",
            "Obsolete member.",
            2,
            "HOLD: build warning applies a strict quality penalty.");
        summary.QualityIssues.Add(issue);
        summary.DecisionImpacts.Add(issue);

        var writer = new ReportWriter();
        var (markdownPath, jsonPath) = await writer.WriteAsync(summary, dir.Path);

        var markdown = await File.ReadAllTextAsync(markdownPath);
        Assert.Contains("Quality Issues And Grading Impact", markdown);
        Assert.Contains("CS0618", markdown);

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath));
        var root = doc.RootElement;
        Assert.Equal(98, root.GetProperty("scorecard").GetProperty("overallScore").GetDouble());
        Assert.Equal(2, root.GetProperty("scorecard").GetProperty("qualityPenalty").GetDouble());
        Assert.Single(root.GetProperty("qualityIssues").EnumerateArray());
        Assert.Single(root.GetProperty("gradingImpacts").EnumerateArray());
        Assert.Single(root.GetProperty("decisionPolicy").GetProperty("impacts").EnumerateArray());
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gaterunner-report-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
