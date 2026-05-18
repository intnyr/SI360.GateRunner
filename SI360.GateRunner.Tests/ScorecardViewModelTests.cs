using SI360.GateRunner.Models;
using SI360.GateRunner.Services;

namespace SI360.GateRunner.Tests;

public sealed class GateScorecardBuilderTests
{
    [Fact]
    public void Build_ShowsEveryCatalogGateAsUnverifiedBeforeRun()
    {
        var items = GateScorecardBuilder.Build(null);

        Assert.Equal(GateCatalog.All.Count, items.Count);
        Assert.Equal("Build Gate", items[0].DisplayName);
        Assert.Equal("Test Inventory Gate", items[^1].DisplayName);
        Assert.All(items, item =>
        {
            Assert.Equal("Unverified", item.Status);
            Assert.Equal("-", item.Grade);
            Assert.Contains("No gate result", item.Evidence, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Build_UsesRealResultEvidenceAndLeavesMissingGatesUnverified()
    {
        var buildGate = GateCatalog.All.Single(g => g.Id == "BuildGate");
        var result = new GateResult
        {
            Definition = buildGate,
            Status = GateStatus.Green,
            Passed = buildGate.ExpectedTestCount,
            Failed = 0,
            Skipped = 0,
            TrxPath = @"E:\SI36020WPF\TestResults\BuildGate.trx"
        };

        var items = GateScorecardBuilder.Build(new[] { result });

        var buildItem = items.Single(i => i.DisplayName == "Build Gate");
        Assert.Equal("Green", buildItem.Status);
        Assert.Equal("A+", buildItem.Grade);
        Assert.Contains($"{buildGate.ExpectedTestCount}/{buildGate.ExpectedTestCount}", buildItem.ResultSummary, StringComparison.Ordinal);
        Assert.Contains("BuildGate.trx", buildItem.Evidence, StringComparison.OrdinalIgnoreCase);

        var missingItem = items.Single(i => i.DisplayName == "Test Inventory Gate");
        Assert.Equal("Unverified", missingItem.Status);
        Assert.Equal("-", missingItem.Grade);
    }

    [Fact]
    public void Build_DoesNotGivePassingGradeWhenExpectedEvidenceIsMissing()
    {
        var inventoryGate = GateCatalog.All.Single(g => g.Id == "TestInventoryGate");
        var result = new GateResult
        {
            Definition = inventoryGate,
            Status = GateStatus.Green,
            Passed = 1,
            Failed = 0,
            Skipped = 0,
            TrxPath = @"E:\SI36020WPF\TestResults\TestInventoryGate.trx"
        };

        var items = GateScorecardBuilder.Build(new[] { result });

        var item = items.Single(i => i.DisplayName == "Test Inventory Gate");
        Assert.Equal("F", item.Grade);
        Assert.Contains("Expected 38, parsed 1", item.Reason, StringComparison.Ordinal);
    }
}
