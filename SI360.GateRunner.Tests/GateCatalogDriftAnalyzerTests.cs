using SI360.GateRunner.Models;
using SI360.GateRunner.Services;

namespace SI360.GateRunner.Tests;

public sealed class GateCatalogDriftAnalyzerTests
{
    [Fact]
    public void DiscoverFromDirectory_FindsGateClassesAndCountsFactsAndTheories()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(
            Path.Combine(dir.Path, "SampleGateTests.cs"),
            """
            namespace SI360.Tests.PreDeploymentGate;

            public class SampleGateTests
            {
                [Fact]
                public void A() { }

                [Theory]
                [InlineData(1)]
                public void B(int value) { }
            }
            """);

        var analyzer = new GateCatalogDriftAnalyzer();
        var gates = analyzer.DiscoverFromDirectory(dir.Path);

        var gate = Assert.Single(gates);
        Assert.Equal("SampleGate", gate.Id);
        Assert.Equal("SampleGateTests", gate.ClassName);
        Assert.Equal(2, gate.TestCount);
    }

    [Fact]
    public void Validate_ReportsCountDriftAndMissingEntries()
    {
        var analyzer = new GateCatalogDriftAnalyzer();
        var discovered = new[]
        {
            new DiscoveredGate("BuildGate", "BuildGateTests", "BuildGateTests.cs", 2),
            new DiscoveredGate("NewGate", "NewGateTests", "NewGateTests.cs", 1)
        };
        var catalog = new[]
        {
            new GateDefinition("BuildGate", "Build Gate", "filter", 1, GateCategory.Build, ""),
            new GateDefinition("SecurityGate", "Security Gate", "filter", 1, GateCategory.Security, "")
        };

        var warnings = analyzer.Validate(discovered, catalog);

        Assert.Contains(warnings, w => w.Code == "GATE_TEST_COUNT_DRIFT");
        Assert.Contains(warnings, w => w.Code == "GATE_MISSING_FROM_SOURCE");
        Assert.Contains(warnings, w => w.Code == "GATE_MISSING_FROM_CATALOG");
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gaterunner-tests-{Guid.NewGuid():N}");
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
