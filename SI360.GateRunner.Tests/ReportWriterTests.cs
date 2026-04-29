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
