using SI360.GateRunner.Services;

namespace SI360.GateRunner.Tests;

public sealed class TestScenarioMatrixLoaderTests
{
    [Fact]
    public void Load_ParsesScenarioMatrixRowsFromMarkdown()
    {
        using var dir = new TempDirectory();
        var solutionPath = Path.Combine(dir.Path, "SI360.slnx");
        Directory.CreateDirectory(Path.Combine(dir.Path, "DOCS"));
        Directory.CreateDirectory(Path.Combine(dir.Path, "SI360.Tests"));
        File.WriteAllText(solutionPath, string.Empty);
        File.WriteAllText(
            Path.Combine(dir.Path, TestScenarioMatrixLoader.DefaultDocumentRelativePath),
            """
            # Unit, Integration, Repository, and Service Test Scenario Matrix

            ## Scenario Matrix

            | Area | Specific Test Scenario | Recommended Test Type | Setup / Inputs | Expected Result | Suggested Target Code | Notes |
            |---|---|---|---|---|---|---|
            | Repository / Service Calculations | SaleSubtotal_SumsActiveItemsOnly | Service test | Sale with active, voided, and removed items | Subtotal includes only active priced items | `SaleService`, `OrderingService` | Keep money values in cents. |
            | SQL / Data Access / Failover | RepositoryQuery_UsesParameterizedInputs | Repository/static test | Repository method receives user input | SQL uses parameters, not string concatenation | `SqlSafetyValidator`, repository classes | Static architecture test is ideal. |

            ## Tracking Notes
            """);
        File.WriteAllText(
            Path.Combine(dir.Path, "SI360.Tests", "MatrixMappedTests.cs"),
            """
            namespace SI360.Tests;

            public sealed class MatrixMappedTests
            {
                [Fact]
                public void SaleSubtotal_SumsActiveItemsOnly()
                {
                }
            }
            """);

        var settings = new RunnerSettings { SolutionPath = solutionPath };
        var document = new TestScenarioMatrixLoader().Load(settings);

        Assert.Empty(document.LoadErrors);
        Assert.Equal(2, document.Items.Count);
        Assert.Equal("SaleSubtotal_SumsActiveItemsOnly", document.Items[0].Scenario);
        Assert.Equal("SaleService, OrderingService", document.Items[0].SuggestedTargetCode);
        Assert.Equal("SI360.Tests.MatrixMappedTests.SaleSubtotal_SumsActiveItemsOnly", document.Items[0].MappedTestFilter);
        Assert.Equal("Repository/static test", document.Items[1].RecommendedTestType);
        Assert.Null(document.Items[1].MappedTestFilter);
    }

    [Fact]
    public void Load_MapsScenarioRowsToSuggestedTargetTestClassWhenExactScenarioMethodIsMissing()
    {
        using var dir = new TempDirectory();
        var solutionPath = Path.Combine(dir.Path, "SI360.slnx");
        Directory.CreateDirectory(Path.Combine(dir.Path, "DOCS"));
        Directory.CreateDirectory(Path.Combine(dir.Path, "SI360.Tests", "Services"));
        File.WriteAllText(solutionPath, string.Empty);
        File.WriteAllText(
            Path.Combine(dir.Path, TestScenarioMatrixLoader.DefaultDocumentRelativePath),
            """
            # Unit, Integration, Repository, and Service Test Scenario Matrix

            ## Scenario Matrix

            | Area | Specific Test Scenario | Recommended Test Type | Setup / Inputs | Expected Result | Suggested Target Code | Notes |
            |---|---|---|---|---|---|---|
            | Repository / Service Calculations | AutoGratuity_AppliesWhenThresholdMet | Service test | Guest count meets rule | Auto gratuity amount is added | `AutoGratuityService` | Include exact threshold boundary. |

            ## Tracking Notes
            """);
        File.WriteAllText(
            Path.Combine(dir.Path, "SI360.Tests", "Services", "AutoGratuityServiceTests.cs"),
            """
            namespace SI360.Tests.Services;

            public sealed class AutoGratuityServiceTests
            {
                [Fact]
                public void CalculatesConfiguredGratuity()
                {
                }
            }
            """);

        var settings = new RunnerSettings { SolutionPath = solutionPath };
        var document = new TestScenarioMatrixLoader().Load(settings);

        Assert.Empty(document.LoadErrors);
        var item = Assert.Single(document.Items);
        Assert.Equal("SI360.Tests.Services.AutoGratuityServiceTests", item.MappedTestFilter);
        Assert.EndsWith("AutoGratuityServiceTests.cs", item.MappedTestSource);
    }

    [Fact]
    public void Load_ReportsMissingDocument()
    {
        using var dir = new TempDirectory();
        var solutionPath = Path.Combine(dir.Path, "SI360.slnx");
        File.WriteAllText(solutionPath, string.Empty);

        var settings = new RunnerSettings { SolutionPath = solutionPath };
        var document = new TestScenarioMatrixLoader().Load(settings);

        Assert.Empty(document.Items);
        Assert.Contains(document.LoadErrors, error => error.Contains("was not found", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Load_ParsesCurrentSi360RepositoryMatrix()
    {
        var solutionPath = @"E:\SI36020WPF\SI360.slnx";
        var matrixPath = @"E:\SI36020WPF\DOCS\Unit-Integration-Service-Test-Scenario-Matrix-2026-05-15.md";
        var testProjectPath = @"E:\SI36020WPF\SI360.Tests\SI360.Tests.csproj";

        Assert.True(File.Exists(solutionPath), $"Expected SI360 solution at {solutionPath}.");
        Assert.True(File.Exists(matrixPath), $"Expected matrix document at {matrixPath}.");
        Assert.True(File.Exists(testProjectPath), $"Expected SI360.Tests project at {testProjectPath}.");

        var settings = new RunnerSettings
        {
            SolutionPath = solutionPath,
            TestProjectPath = testProjectPath
        };

        var document = new TestScenarioMatrixLoader().Load(settings, matrixPath);
        Assert.Empty(document.LoadErrors);
        Assert.Equal(61, document.Items.Count);
        Assert.True(
            document.Items.Count(item => !string.IsNullOrWhiteSpace(item.MappedTestFilter)) > 0,
            "current SI360 matrix should map at least some rows to executable tests");
        Assert.Contains(document.Items, item => item.Scenario == "SaleSubtotal_SumsActiveItemsOnly");
        Assert.Contains(document.Items, item => item.Scenario == "RepositoryQuery_UsesParameterizedInputs");
        Assert.Contains(document.Items, item => item.Scenario == "OfflineCredit_QueuesCaptureWhenOffline");
        Assert.All(
            document.Items.Where(item => !string.IsNullOrWhiteSpace(item.MappedTestFilter)),
            item =>
            {
                Assert.True(File.Exists(item.MappedTestSource), $"Mapped source should exist for {item.Scenario}: {item.MappedTestSource}");
            });
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gaterunner-matrix-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for test temp files.
            }
        }
    }
}
