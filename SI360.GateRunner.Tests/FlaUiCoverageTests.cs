using System.Text.Json;
using SI360.GateRunner.Models;
using SI360.GateRunner.Services;

namespace SI360.GateRunner.Tests;

public sealed class FlaUiCoverageTests
{
    [Fact]
    public void ManifestLoader_LoadsValidManifestAndRejectsDuplicateIds()
    {
        using var dir = new TempDirectory();
        var settings = CreateSettings(dir.Path);
        WriteSi360File(dir.Path, "DOCS/order.md");
        WriteSi360File(dir.Path, "SI360.UITests/Functional/OrderLifecycleFunctionalTests.cs");
        WriteSi360File(dir.Path, "SI360.UITests/Functional/ReportsFunctionalTests.cs");

        var manifestPath = Path.Combine(dir.Path, "manifest.json");
        File.WriteAllText(manifestPath, ManifestJson(
            """
            {
              "id": "sign-on",
              "name": "Sign on",
              "group": "OrderTakingProcedures",
              "automationStatus": "Automated",
              "verificationLevel": "Discoverable",
              "testFilters": [ "Functional_01_Login_To_Room_Should_Succeed" ],
              "sourceFiles": [ "SI360.UITests/Functional/OrderLifecycleFunctionalTests.cs" ]
            }
            """,
            """
            {
              "id": "employee-cashout-report",
              "name": "Employee Cashout Report",
              "group": "UserFunctionsAndDiningRoomScenarios",
              "automationStatus": "Automated",
              "verificationLevel": "ReportState",
              "testFilters": [ "Functional_40_Employee_Cashout_Report_Should_Open_And_Show_Report_State" ],
              "sourceFiles": [ "SI360.UITests/Functional/ReportsFunctionalTests.cs" ]
            }
            """));

        var loader = new FlaUiCoverageManifestLoader();
        var result = loader.Load(manifestPath, settings);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
        Assert.Equal(2, result.Manifest!.Sections.Count);

        File.WriteAllText(manifestPath, ManifestJson(
            """
            {
              "id": "duplicate",
              "name": "Sign on",
              "group": "OrderTakingProcedures",
              "automationStatus": "Automated",
              "verificationLevel": "Discoverable",
              "sourceFiles": [ "SI360.UITests/Functional/OrderLifecycleFunctionalTests.cs" ]
            }
            """,
            """
            {
              "id": "duplicate",
              "name": "Employee Cashout Report",
              "group": "UserFunctionsAndDiningRoomScenarios",
              "automationStatus": "Automated",
              "verificationLevel": "ReportState",
              "sourceFiles": [ "SI360.UITests/Functional/ReportsFunctionalTests.cs" ]
            }
            """));

        var invalid = loader.Load(manifestPath, settings);

        Assert.False(invalid.IsValid);
        Assert.Contains(invalid.Errors, e => e.Contains("Duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RepositoryManifest_ContainsRequiredCoverageGroupsAndScenarios()
    {
        var manifestPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "flaui-coverage-manifest.json"));

        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var sections = doc.RootElement.GetProperty("sections").EnumerateArray().ToList();

        Assert.Equal(2, sections.Count);
        AssertManifestSection(
            sections[0],
            "OrderTakingProcedures",
            "ORDER TAKING PROCEDURES",
            new[]
            {
                "Sign on",
                "Select profile",
                "Start a Table",
                "Assign Customers to A Seat",
                "Meal Type Selection",
                "Additional Options - Reassign Seat",
                "Additional Options - Remove Resident",
                "Additional Options - Replace Customer",
                "Public Customers",
                "Adding Additional Seats",
                "Add Order",
                "Screen Layout",
                "Select Menu Items",
                "Modifying Orders",
                "Forced Modifiers",
                "Modify Button",
                "Message to Kitchen",
                "Send Order",
                "Delete Items From an Order",
                "Move Items to Another Seat",
                "Check Settlement",
                "Auto Pay",
                "Pay Check",
                "Pay Check - Cash",
                "Pay Check - Direct Billing",
                "Pay Check - Meal Plan",
                "Split Payment"
            });

        AssertManifestSection(
            sections[1],
            "UserFunctionsAndDiningRoomScenarios",
            "USER FUNCTIONS AND DINING ROOM SCENARIOS",
            new[]
            {
                "User Functions",
                "Sell Gift Card",
                "Sell Multiple Gift Cards",
                "Adding Tips to a Gift Card",
                "Check Gift Card Balance",
                "Adding Funds to a Gift Card",
                "Redeeming Gift Card",
                "Closed Check",
                "Combine Check (Merging Orders of More Than One Sale Together)",
                "Print Separate Checks",
                "Enter Charge Tip",
                "Print Check",
                "Split Item",
                "Reorder",
                "Reset Service Profile",
                "Restart",
                "Server Sales Report",
                "Employee Cashout Report",
                "Transfer Check",
                "Change Order Types"
            });
    }

    [Fact]
    public void CoverageService_ComputesPassedBlockedNeedsReviewAndNotStarted()
    {
        using var dir = new TempDirectory();
        var settings = CreateSettings(dir.Path);
        WriteSi360File(dir.Path, "DOCS/order.md");
        WriteSi360File(dir.Path, "SI360.UITests/Functional/OrderLifecycleFunctionalTests.cs");
        WriteSi360File(dir.Path, "SI360.UITests/Functional/ReportsFunctionalTests.cs");
        var manifestPath = Path.Combine(dir.Path, "manifest.json");
        File.WriteAllText(manifestPath, ManifestJson(
            """
            {
              "id": "sign-on",
              "name": "Sign on",
              "group": "OrderTakingProcedures",
              "automationStatus": "Automated",
              "verificationLevel": "Discoverable",
              "testFilters": [ "Functional_01_Login_To_Room_Should_Succeed" ],
              "sourceFiles": [ "SI360.UITests/Functional/OrderLifecycleFunctionalTests.cs" ]
            }
            """,
            """
            {
              "id": "employee-cashout-report",
              "name": "Employee Cashout Report",
              "group": "UserFunctionsAndDiningRoomScenarios",
              "automationStatus": "Automated",
              "verificationLevel": "ReportState",
              "testFilters": [ "Functional_40_Employee_Cashout_Report_Should_Open_And_Show_Report_State" ],
              "sourceFiles": [ "SI360.UITests/Functional/ReportsFunctionalTests.cs" ]
            },
            {
              "id": "restart",
              "name": "Restart",
              "group": "UserFunctionsAndDiningRoomScenarios",
              "automationStatus": "NeedsReview",
              "verificationLevel": "NeedsReview",
              "testFilters": [ "AppFixture.RestartApp" ],
              "sourceFiles": [ "SI360.UITests/Functional/ReportsFunctionalTests.cs" ]
            },
            {
              "id": "reorder",
              "name": "Reorder",
              "group": "UserFunctionsAndDiningRoomScenarios",
              "automationStatus": "NotAutomated",
              "verificationLevel": "None"
            }
            """));

        Directory.CreateDirectory(settings.ResultsDirectory);
        File.WriteAllText(Path.Combine(settings.ResultsDirectory, "flaui.trx"), Trx(
            ("Functional_01_Login_To_Room_Should_Succeed", "Passed", null),
            ("Functional_40_Employee_Cashout_Report_Should_Open_And_Show_Report_State", "Failed", "Unable to acquire current main window at SI360.UITests.Core.AppFixture and WindowTracker")));

        var service = new FlaUiCoverageService(new TestManifestLoader(manifestPath), new TrxResultParser());
        var run = service.Load(settings);
        var items = run.Sections.SelectMany(s => s.Items).ToDictionary(i => i.Item.Id);

        Assert.Equal(FlaUiCoverageStatus.Passed, items["sign-on"].Status);
        Assert.Equal(FlaUiCoverageStatus.Blocked, items["employee-cashout-report"].Status);
        Assert.Equal(FlaUiCoverageStatus.NeedsReview, items["restart"].Status);
        Assert.Equal(FlaUiCoverageStatus.NotStarted, items["reorder"].Status);
        Assert.Equal(4, run.Summary.Total);
        Assert.Equal(1, run.Summary.Passed);
        Assert.Equal(1, run.Summary.Blocked);
        Assert.Equal(1, run.Summary.NeedsReview);
        Assert.Equal(1, run.Summary.NotStarted);
    }

    [Fact]
    public void CoverageService_ClassifiesMissingUiAppPathAsBlocked()
    {
        using var dir = new TempDirectory();
        var settings = CreateSettings(dir.Path);
        WriteSi360File(dir.Path, "DOCS/order.md");
        WriteSi360File(dir.Path, "SI360.UITests/Functional/OrderLifecycleFunctionalTests.cs");
        var manifestPath = Path.Combine(dir.Path, "manifest.json");
        File.WriteAllText(manifestPath, ManifestJson(
            """
            {
              "id": "order-taking-sign-on",
              "name": "Sign on",
              "group": "OrderTakingProcedures",
              "automationStatus": "Automated",
              "verificationLevel": "Discoverable",
              "testFilters": [ "Functional_01_Login_To_Room_Should_Succeed" ],
              "sourceFiles": [ "SI360.UITests/Functional/OrderLifecycleFunctionalTests.cs" ]
            }
            """,
            """
            {
              "id": "reorder",
              "name": "Reorder",
              "group": "UserFunctionsAndDiningRoomScenarios",
              "automationStatus": "NotAutomated",
              "verificationLevel": "None"
            }
            """));

        Directory.CreateDirectory(settings.ResultsDirectory);
        File.WriteAllText(Path.Combine(settings.ResultsDirectory, "flaui.trx"), Trx(
            ("SI360.UITests.Functional.OrderLifecycleFunctionalTests.Functional_01_Login_To_Room_Should_Succeed", "Failed", "AppFixture is not ready. SI360_UI_APP_PATH is required for UI tests.")));

        var service = new FlaUiCoverageService(new TestManifestLoader(manifestPath), new TrxResultParser());
        var run = service.Load(settings);
        var item = run.Sections.SelectMany(s => s.Items).Single(i => i.Item.Id == "order-taking-sign-on");

        Assert.Equal(FlaUiCoverageStatus.Blocked, item.Status);
        Assert.Equal(FlaUiExecutionStatus.Blocked, item.ExecutionStatus);
    }

    [Fact]
    public void CoverageService_ClassifiesMissingValidPinAsBlocked()
    {
        using var dir = new TempDirectory();
        var settings = CreateSettings(dir.Path);
        WriteSi360File(dir.Path, "DOCS/order.md");
        WriteSi360File(dir.Path, "SI360.UITests/Functional/OrderLifecycleFunctionalTests.cs");
        var manifestPath = Path.Combine(dir.Path, "manifest.json");
        File.WriteAllText(manifestPath, ManifestJson(
            """
            {
              "id": "order-taking-sign-on",
              "name": "Sign on",
              "group": "OrderTakingProcedures",
              "automationStatus": "Automated",
              "verificationLevel": "Discoverable",
              "testFilters": [ "Functional_01_Login_To_Room_Should_Succeed" ],
              "sourceFiles": [ "SI360.UITests/Functional/OrderLifecycleFunctionalTests.cs" ]
            }
            """,
            """
            {
              "id": "reorder",
              "name": "Reorder",
              "group": "UserFunctionsAndDiningRoomScenarios",
              "automationStatus": "NotAutomated",
              "verificationLevel": "None"
            }
            """));

        Directory.CreateDirectory(settings.ResultsDirectory);
        File.WriteAllText(Path.Combine(settings.ResultsDirectory, "flaui.trx"), Trx(
            ("SI360.UITests.Functional.OrderLifecycleFunctionalTests.Functional_01_Login_To_Room_Should_Succeed", "Failed", "SI360_UI_VALID_PIN is required for Auth=Required tests.")));

        var service = new FlaUiCoverageService(new TestManifestLoader(manifestPath), new TrxResultParser());
        var run = service.Load(settings);
        var item = run.Sections.SelectMany(s => s.Items).Single(i => i.Item.Id == "order-taking-sign-on");

        Assert.Equal(FlaUiCoverageStatus.Blocked, item.Status);
        Assert.Equal("SI360_UI_VALID_PIN is required for Auth=Required FlaUI tests.", item.BlockerReason);
    }

    [Fact]
    public void CoverageService_ClassifiesPinLoginNotReadyAsBlocked()
    {
        using var dir = new TempDirectory();
        var settings = CreateSettings(dir.Path);
        WriteSi360File(dir.Path, "DOCS/order.md");
        WriteSi360File(dir.Path, "SI360.UITests/Functional/OrderLifecycleFunctionalTests.cs");
        var manifestPath = Path.Combine(dir.Path, "manifest.json");
        File.WriteAllText(manifestPath, ManifestJson(
            """
            {
              "id": "order-taking-sign-on",
              "name": "Sign on",
              "group": "OrderTakingProcedures",
              "automationStatus": "Automated",
              "verificationLevel": "Discoverable",
              "testFilters": [ "Functional_01_Login_To_Room_Should_Succeed" ],
              "sourceFiles": [ "SI360.UITests/Functional/OrderLifecycleFunctionalTests.cs" ]
            }
            """,
            """
            {
              "id": "reorder",
              "name": "Reorder",
              "group": "UserFunctionsAndDiningRoomScenarios",
              "automationStatus": "NotAutomated",
              "verificationLevel": "None"
            }
            """));

        Directory.CreateDirectory(settings.ResultsDirectory);
        File.WriteAllText(Path.Combine(settings.ResultsDirectory, "flaui.trx"), Trx(
            ("SI360.UITests.Functional.OrderLifecycleFunctionalTests.Functional_01_Login_To_Room_Should_Succeed", "Failed", "PIN login page was not ready.")));

        var service = new FlaUiCoverageService(new TestManifestLoader(manifestPath), new TrxResultParser());
        var run = service.Load(settings);
        var item = run.Sections.SelectMany(s => s.Items).Single(i => i.Item.Id == "order-taking-sign-on");

        Assert.Equal(FlaUiCoverageStatus.Blocked, item.Status);
        Assert.Equal("SI360 launched, but the PIN login page was not ready for FlaUI automation.", item.BlockerReason);
    }

    [Fact]
    public async Task CoverageRunner_RunsSelectedScenarioAndWritesCoverageArtifacts()
    {
        using var dir = new TempDirectory();
        var settings = CreateSettings(dir.Path);
        WriteSi360File(dir.Path, "DOCS/order.md");
        WriteSi360File(dir.Path, "SI360.UITests/Functional/OrderLifecycleFunctionalTests.cs");
        var manifestPath = Path.Combine(dir.Path, "manifest.json");
        File.WriteAllText(manifestPath, ManifestJson(
            """
            {
              "id": "sign-on",
              "name": "Sign on",
              "group": "OrderTakingProcedures",
              "automationStatus": "Automated",
              "verificationLevel": "Discoverable",
              "testFilters": [ "Functional_01_Login_To_Room_Should_Succeed" ],
              "sourceFiles": [ "SI360.UITests/Functional/OrderLifecycleFunctionalTests.cs" ]
            }
            """,
            """
            {
              "id": "reorder",
              "name": "Reorder",
              "group": "UserFunctionsAndDiningRoomScenarios",
              "automationStatus": "NotAutomated",
              "verificationLevel": "None"
            }
            """));
        var processRunner = new CaptureProcessRunner();
        var coverageService = new FlaUiCoverageService(new TestManifestLoader(manifestPath), new TrxResultParser());
        var runner = new FlaUiCoverageRunner(settings, coverageService, processRunner);

        var result = await runner.RunAsync(
            new FlaUiCoverageRunRequest { ItemIds = new[] { "sign-on" } },
            null,
            CancellationToken.None);

        Assert.True(result.StartedProcess);
        Assert.Equal(1, result.MatchedCount);
        Assert.Equal(1, result.RunnableCount);
        Assert.EndsWith(".trx", result.TrxPath);
        Assert.Contains("FlaUiCoverageRun_", result.RunDirectory);
        Assert.NotNull(CaptureProcessRunner.LastCommand);
        Assert.Contains("--filter \"FullyQualifiedName~Functional_01_Login_To_Room_Should_Succeed\"", CaptureProcessRunner.LastCommand!.Arguments);
        Assert.Contains("--results-directory", CaptureProcessRunner.LastCommand.Arguments);
    }

    [Fact]
    public async Task CoverageRunner_MatchesScenarioAliases()
    {
        using var dir = new TempDirectory();
        var settings = CreateSettings(dir.Path);
        WriteSi360File(dir.Path, "DOCS/order.md");
        WriteSi360File(dir.Path, "SI360.UITests/Functional/OrderLifecycleFunctionalTests.cs");
        var manifestPath = Path.Combine(dir.Path, "manifest.json");
        File.WriteAllText(manifestPath, ManifestJson(
            """
            {
              "id": "order-taking-sign-on",
              "name": "Sign on",
              "group": "OrderTakingProcedures",
              "automationStatus": "Automated",
              "verificationLevel": "Discoverable",
              "testFilters": [ "Functional_01_Login_To_Room_Should_Succeed" ],
              "sourceFiles": [ "SI360.UITests/Functional/OrderLifecycleFunctionalTests.cs" ]
            }
            """,
            """
            {
              "id": "reorder",
              "name": "Reorder",
              "group": "UserFunctionsAndDiningRoomScenarios",
              "automationStatus": "NotAutomated",
              "verificationLevel": "None"
            }
            """));
        CaptureProcessRunner.LastCommand = null;
        var coverageService = new FlaUiCoverageService(new TestManifestLoader(manifestPath), new TrxResultParser());
        var runner = new FlaUiCoverageRunner(settings, coverageService, new CaptureProcessRunner());

        var result = await runner.RunAsync(
            new FlaUiCoverageRunRequest { Scenario = "sign-on" },
            null,
            CancellationToken.None);

        Assert.True(result.StartedProcess);
        Assert.Equal(1, result.MatchedCount);
        Assert.Contains("Functional_01_Login_To_Room_Should_Succeed", CaptureProcessRunner.LastCommand!.Arguments);
    }

    [Fact]
    public async Task CoverageRunner_SkipsNotAutomatedScenarioWithoutStartingProcess()
    {
        using var dir = new TempDirectory();
        var settings = CreateSettings(dir.Path);
        WriteSi360File(dir.Path, "DOCS/order.md");
        WriteSi360File(dir.Path, "SI360.UITests/Functional/OrderLifecycleFunctionalTests.cs");
        var manifestPath = Path.Combine(dir.Path, "manifest.json");
        File.WriteAllText(manifestPath, ManifestJson(
            """
            {
              "id": "sign-on",
              "name": "Sign on",
              "group": "OrderTakingProcedures",
              "automationStatus": "Automated",
              "verificationLevel": "Discoverable",
              "testFilters": [ "Functional_01_Login_To_Room_Should_Succeed" ],
              "sourceFiles": [ "SI360.UITests/Functional/OrderLifecycleFunctionalTests.cs" ]
            }
            """,
            """
            {
              "id": "reorder",
              "name": "Reorder",
              "group": "UserFunctionsAndDiningRoomScenarios",
              "automationStatus": "NotAutomated",
              "verificationLevel": "None"
            }
            """));
        CaptureProcessRunner.LastCommand = null;
        var coverageService = new FlaUiCoverageService(new TestManifestLoader(manifestPath), new TrxResultParser());
        var runner = new FlaUiCoverageRunner(settings, coverageService, new CaptureProcessRunner());

        var result = await runner.RunAsync(
            new FlaUiCoverageRunRequest { ItemIds = new[] { "reorder" } },
            null,
            CancellationToken.None);

        Assert.False(result.StartedProcess);
        Assert.Single(result.SkippedItems);
        Assert.Contains(result.Errors, e => e.Contains("No runnable", StringComparison.OrdinalIgnoreCase));
        Assert.Null(CaptureProcessRunner.LastCommand);
    }

    [Fact]
    public async Task ReportWriter_IncludesFlaUiCoverage()
    {
        using var dir = new TempDirectory();
        var summary = new RunSummary
        {
            StartedAt = new DateTime(2026, 5, 6, 1, 2, 3, DateTimeKind.Utc),
            FlaUiCoverage =
            {
                ManifestPath = "flaui-coverage-manifest.json",
                Summary = new FlaUiCoverageSummary { Total = 1, Automated = 1, Passed = 1 }
            }
        };
        var section = new FlaUiCoverageSectionResult
        {
            Group = FlaUiCoverageGroup.OrderTakingProcedures,
            Name = "ORDER TAKING PROCEDURES"
        };
        section.Items.Add(new FlaUiCoverageItemResult
        {
            Item = new FlaUiCoverageItem
            {
                Id = "sign-on",
                Name = "Sign on",
                Group = FlaUiCoverageGroup.OrderTakingProcedures,
                AutomationStatus = FlaUiAutomationStatus.Automated,
                VerificationLevel = "Discoverable"
            },
            Status = FlaUiCoverageStatus.Passed,
            ExecutionStatus = FlaUiExecutionStatus.Passed
        });
        summary.FlaUiCoverage.Sections.Add(section);

        var writer = new ReportWriter();
        var (markdownPath, jsonPath) = await writer.WriteAsync(summary, dir.Path);

        Assert.Contains("FlaUI Coverage", await File.ReadAllTextAsync(markdownPath));
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath));
        var coverage = doc.RootElement.GetProperty("flauiCoverage");
        Assert.Equal(1, coverage.GetProperty("summary").GetProperty("Total").GetInt32());
        Assert.Equal("Sign on", coverage.GetProperty("sections")[0].GetProperty("items")[0].GetProperty("name").GetString());
    }

    private static RunnerSettings CreateSettings(string root)
    {
        File.WriteAllText(Path.Combine(root, "SI360.slnx"), string.Empty);
        WriteSi360File(root, "SI360.Tests/SI360.Tests.csproj");
        WriteSi360File(root, "SI360.UITests/SI360.UITests.csproj");
        WriteSi360File(root, "SI360.UI/bin/Debug/net8.0-windows/SI360.UI.exe");
        return new RunnerSettings
        {
            SolutionPath = Path.Combine(root, "SI360.slnx"),
            TestProjectPath = Path.Combine(root, "SI360.Tests", "SI360.Tests.csproj"),
            FlaUiTestProjectPath = Path.Combine(root, "SI360.UITests", "SI360.UITests.csproj"),
            Si360UiAppPath = Path.Combine(root, "SI360.UI", "bin", "Debug", "net8.0-windows", "SI360.UI.exe"),
            Si360UiValidPin = "7458",
            ResultsDirectory = Path.Combine(root, "TestResults")
        };
    }

    private static void WriteSi360File(string root, string relativePath)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
    }

    private static string ManifestJson(string orderTakingItems, string userFunctionItems) => $$"""
        {
          "schemaVersion": "1.0",
          "sections": [
            {
              "group": "OrderTakingProcedures",
              "name": "ORDER TAKING PROCEDURES",
              "sourceDocument": "DOCS/order.md",
              "items": [ {{orderTakingItems}} ]
            },
            {
              "group": "UserFunctionsAndDiningRoomScenarios",
              "name": "USER FUNCTIONS AND DINING ROOM SCENARIOS",
              "items": [ {{userFunctionItems}} ]
            }
          ]
        }
        """;

    private static string Trx(params (string Name, string Outcome, string? Error)[] results)
    {
        var rows = string.Join(Environment.NewLine, results.Select(r => $"""
            <UnitTestResult testName="{r.Name}" outcome="{r.Outcome}" duration="00:00:01">
              <Output>
                <ErrorInfo>
                  <Message>{System.Security.SecurityElement.Escape(r.Error ?? string.Empty)}</Message>
                  <StackTrace>{System.Security.SecurityElement.Escape(r.Error ?? string.Empty)}</StackTrace>
                </ErrorInfo>
              </Output>
            </UnitTestResult>
            """));
        return $$"""
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
                {{rows}}
              </Results>
            </TestRun>
            """;
    }

    private static void AssertManifestSection(
        JsonElement section,
        string expectedGroup,
        string expectedName,
        IReadOnlyList<string> expectedItems)
    {
        Assert.Equal(expectedGroup, section.GetProperty("group").GetString());
        Assert.Equal(expectedName, section.GetProperty("name").GetString());
        var actual = section.GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("name").GetString() ?? string.Empty)
            .ToList();
        Assert.Equal(expectedItems, actual);
    }

    private sealed class TestManifestLoader : IFlaUiCoverageManifestLoader
    {
        private readonly FlaUiCoverageManifestLoader _inner = new();
        private readonly string _path;

        public TestManifestLoader(string path)
        {
            _path = path;
        }

        public string ResolveDefaultManifestPath() => _path;

        public FlaUiCoverageValidationResult Load(string? manifestPath, RunnerSettings settings) =>
            _inner.Load(manifestPath ?? _path, settings);
    }

    private sealed class CaptureProcessRunner : IProcessRunner
    {
        public static ProcessCommand? LastCommand { get; set; }

        public Task<ProcessRunResult> RunAsync(
            ProcessCommand command,
            IProgress<string>? log,
            CancellationToken cancellationToken)
        {
            LastCommand = command;
            log?.Report("captured flaui coverage run");
            return Task.FromResult(new ProcessRunResult(0, "captured", string.Empty, false, false, command.ArtifactDirectory));
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gaterunner-flaui-tests-{Guid.NewGuid():N}");
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
