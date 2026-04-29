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
    public void Decide_ReturnsNoGo_WhenBuildErrorsExist()
    {
        var summary = SummaryWithScore(100, 100);
        summary.BuildErrors.Add(new BuildError("A.cs", 1, 2, "CS1001", "Broken"));

        var result = _policy.Decide(summary);

        Assert.Equal(DeployDecision.NoGo, result.Decision);
        Assert.Contains("build produced 1 error", result.Rationale);
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
