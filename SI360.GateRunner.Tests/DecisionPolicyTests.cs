using SI360.GateRunner.Models;
using SI360.GateRunner.Services;

namespace SI360.GateRunner.Tests;

public sealed class DecisionPolicyTests
{
    private readonly DecisionPolicy _policy = new();

    [Fact]
    public void Decide_ReturnsGo_WhenScoreMeetsThresholdAndAllGatesGreen()
    {
        var summary = SummaryWithScore(100, 100);
        summary.GateResults.Add(Gate("BuildGate", GateStatus.Green));
        summary.GateResults.Add(Gate("SecurityGate", GateStatus.Green));

        var result = _policy.Decide(summary);

        Assert.Equal(DeployDecision.Go, result.Decision);
        Assert.Equal("AllGatesAndScorecard", result.PolicyName);
        Assert.Contains("all gates are green", result.Rationale);
    }

    [Fact]
    public void Decide_ReturnsHold_WhenScorePassesButAnyGateIsYellow()
    {
        var summary = SummaryWithScore(100, 100);
        summary.GateResults.Add(Gate("BuildGate", GateStatus.Green));
        summary.GateResults.Add(Gate("ProductionSafetyGate", GateStatus.Yellow));

        var result = _policy.Decide(summary);

        Assert.Equal(DeployDecision.Hold, result.Decision);
        Assert.Contains("ProductionSafetyGate", result.Rationale);
    }

    [Fact]
    public void Decide_ReturnsNoGo_WhenAnyGateIsRed()
    {
        var summary = SummaryWithScore(100, 100);
        summary.GateResults.Add(Gate("BuildGate", GateStatus.Green));
        summary.GateResults.Add(Gate("SecurityGate", GateStatus.Red));

        var result = _policy.Decide(summary);

        Assert.Equal(DeployDecision.NoGo, result.Decision);
        Assert.Contains("SecurityGate", result.Rationale);
    }

    [Fact]
    public void Decide_ReturnsHold_WhenCatalogWarningsExist()
    {
        var summary = SummaryWithScore(100, 100);
        summary.GateResults.Add(Gate("BuildGate", GateStatus.Green));
        summary.GateCatalogWarnings.Add(new GateCatalogWarning("GATE_TEST_COUNT_DRIFT", "Count changed."));

        var result = _policy.Decide(summary);

        Assert.Equal(DeployDecision.Hold, result.Decision);
        Assert.Contains("warning issue", result.Rationale);
        Assert.Contains("GATE_TEST_COUNT_DRIFT", result.Rationale);
    }

    [Fact]
    public void Decide_ReturnsNoGo_WhenBuildErrorsExist()
    {
        var summary = SummaryWithScore(100, 100);
        summary.BuildErrors.Add(new BuildError("A.cs", 1, 2, "CS1001", "Broken"));

        var result = _policy.Decide(summary);

        Assert.Equal(DeployDecision.NoGo, result.Decision);
        Assert.Contains("error issue", result.Rationale);
    }

    [Fact]
    public void Decide_ReturnsHold_WhenBuildWarningExists()
    {
        var summary = SummaryWithScore(100, 100);
        summary.GateResults.Add(Gate("BuildGate", GateStatus.Green));
        summary.QualityIssues.Add(new QualityIssue(
            "build:warning:CS0618",
            QualityIssueSeverity.Warning,
            QualityIssueSource.Build,
            "A.cs:10",
            "CS0618",
            "Obsolete member.",
            2,
            "HOLD: build warning applies a strict quality penalty."));

        var result = _policy.Decide(summary);

        Assert.Equal(DeployDecision.Hold, result.Decision);
        Assert.Contains("warning issue", result.Rationale);
        Assert.Equal(98, summary.Scorecard.OverallScore);
        Assert.Single(result.Impacts);
    }

    [Fact]
    public void Decide_ReturnsNoGo_WhenRuntimeReadinessIsNotReady()
    {
        var summary = SummaryWithScore(100, 100);
        summary.GateResults.Add(Gate("BuildGate", GateStatus.Green));
        summary.RuntimeReadiness = RuntimeReadinessDecision.NotReady;
        summary.RuntimeReadinessRationale = "One or more synthetic probes failed.";
        summary.SyntheticProbes.Add(new SyntheticProbeResult(
            "si360-health",
            "SI360 health summary",
            "https://si360.example.test/health",
            "phase-1-readonly",
            SyntheticProbeStatus.Failed,
            10,
            "HTTP 500 Internal Server Error"));

        var result = _policy.Decide(summary);

        Assert.Equal(DeployDecision.NoGo, result.Decision);
        Assert.Contains("probe:si360-health", result.Rationale);
        Assert.Contains(result.Impacts, i => i.Source == QualityIssueSource.SyntheticProbe);
    }

    [Fact]
    public void Decide_ReturnsHold_WhenRuntimeReadinessIsUnknown()
    {
        var summary = SummaryWithScore(100, 100);
        summary.GateResults.Add(Gate("BuildGate", GateStatus.Green));
        summary.RuntimeReadiness = RuntimeReadinessDecision.Unknown;
        summary.RuntimeReadinessRationale = "Deployment metadata was not configured.";

        var result = _policy.Decide(summary);

        Assert.Equal(DeployDecision.Hold, result.Decision);
        Assert.Contains("runtime-readiness:unknown", result.Rationale);
        Assert.Equal(98, summary.Scorecard.OverallScore);
    }

    [Fact]
    public void Decide_ReturnsHold_WhenAllGatesGreenButScoreIsBelowGoThreshold()
    {
        var summary = SummaryWithScore(100, 70);
        summary.GateResults.Add(Gate("BuildGate", GateStatus.Green));

        var result = _policy.Decide(summary);

        Assert.Equal(DeployDecision.Hold, result.Decision);
        Assert.Contains("below the GO threshold", result.Rationale);
    }

    private static RunSummary SummaryWithScore(double scenarioScore, double probabilisticScore) =>
        new()
        {
            StartedAt = DateTime.UtcNow,
            RuntimeReadiness = RuntimeReadinessDecision.Ready,
            Scorecard = new Scorecard
            {
                ScenarioScore = scenarioScore,
                ProbabilisticScore = probabilisticScore
            }
        };

    private static GateResult Gate(string id, GateStatus status) =>
        new()
        {
            Definition = new GateDefinition(
                id,
                id,
                $"FullyQualifiedName~{id}Tests",
                1,
                GateCategory.Build,
                string.Empty),
            Status = status,
            Passed = status is GateStatus.Green or GateStatus.Yellow ? 1 : 0,
            Failed = status is GateStatus.Red or GateStatus.Yellow ? 1 : 0
        };
}
