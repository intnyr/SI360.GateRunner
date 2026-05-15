using SI360.GateRunner.Services;

namespace SI360.GateRunner.Tests;

public sealed class FlaUiScenarioMatrixLoaderTests
{
    [Fact]
    public void Load_ParsesFlaUiScenarioMatrixRowsFromMarkdown()
    {
        using var dir = new TempDirectory();
        var solutionPath = Path.Combine(dir.Path, "SI360.slnx");
        Directory.CreateDirectory(Path.Combine(dir.Path, "DOCS"));
        File.WriteAllText(solutionPath, string.Empty);
        File.WriteAllText(
            Path.Combine(dir.Path, FlaUiScenarioMatrixLoader.DefaultDocumentRelativePath),
            """
            # FlaUI Specific Scenario Matrix

            ## Scenario Matrix

            | Priority | Parent Scenario | Specific FlaUI Scenario | Description | Primary UI Areas / Controls | Verification Target | Suggested Evidence |
            |---|---|---|---|---|---|---|
            | High | Sign On Screen | Valid PIN Authenticates | Enter valid PIN and reach next screen. | `PinButton_0..9`, `LoginButton` | Login screen no longer active. | TRX, screenshot |
            | Medium | Gift Card Lifecycle | Sell Gift Card Dialog Opens | User Function > Sell Gift Card. | `SellGiftCardTypeComboBox`, `SellCardButton` | Dialog/numpad controls visible. | TRX, UI tree |
            | Low | Accessibility/Keyboard Smoke | PIN Tab Navigation Cycles | Tab through PIN screen. | PIN buttons, login, clear/delete | Focus order cycles predictably. | TRX, UI tree |

            ## Automation Guidance
            """);

        var settings = new RunnerSettings { SolutionPath = solutionPath };
        var document = new FlaUiScenarioMatrixLoader().Load(settings);

        Assert.Empty(document.LoadErrors);
        Assert.Equal(3, document.Items.Count);
        Assert.Equal("High", document.Items[0].Priority);
        Assert.Equal("Sign On Screen", document.Items[0].ParentScenario);
        Assert.Equal("Valid PIN Authenticates", document.Items[0].SpecificScenario);
        Assert.Equal(0, document.Items[0].SourceOrder);
        Assert.Equal(1, document.Items[1].SourceOrder);
        Assert.Equal(2, document.Items[2].SourceOrder);
        Assert.Equal("PinButton_0..9, LoginButton", document.Items[0].PrimaryUiAreasControls);
    }

    [Fact]
    public void Load_ReportsMissingDocument()
    {
        using var dir = new TempDirectory();
        var solutionPath = Path.Combine(dir.Path, "SI360.slnx");
        File.WriteAllText(solutionPath, string.Empty);

        var settings = new RunnerSettings { SolutionPath = solutionPath };
        var document = new FlaUiScenarioMatrixLoader().Load(settings);

        Assert.Empty(document.Items);
        Assert.Contains(document.LoadErrors, error => error.Contains("was not found", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Load_ParsesCurrentSi360FlaUiScenarioMatrixByPriority()
    {
        var solutionPath = @"E:\SI36020WPF\SI360.slnx";
        var matrixPath = @"E:\SI36020WPF\DOCS\FlaUI-Specific-Scenario-Matrix-2026-05-15.md";

        Assert.True(File.Exists(solutionPath), $"Expected SI360 solution at {solutionPath}.");
        Assert.True(File.Exists(matrixPath), $"Expected FlaUI scenario matrix document at {matrixPath}.");

        var settings = new RunnerSettings { SolutionPath = solutionPath };
        var document = new FlaUiScenarioMatrixLoader().Load(settings, matrixPath);

        Assert.Empty(document.LoadErrors);
        Assert.Equal(65, document.Items.Count);
        Assert.Equal(36, document.Items.Count(item => item.Priority == "High"));
        Assert.Equal(20, document.Items.Count(item => item.Priority == "Medium"));
        Assert.Equal(9, document.Items.Count(item => item.Priority == "Low"));
        Assert.All(document.Items, item => Assert.Contains(item.Priority, new[] { "High", "Medium", "Low" }));
        Assert.Equal(Enumerable.Range(0, document.Items.Count), document.Items.Select(item => item.SourceOrder));
        Assert.True(document.Items.Count(item => !string.IsNullOrWhiteSpace(item.MappedTestFilter)) > 0);
        Assert.Contains(document.Items, item =>
            item.SpecificScenario == "Valid PIN Authenticates" &&
            item.MappedTestFilter?.EndsWith("Sign_On_Screen_Should_Authenticate_To_Service_Profile_Or_Room_Selection", StringComparison.Ordinal) == true);
        Assert.Contains(document.Items, item =>
            item.SpecificScenario == "Sell Gift Card Dialog Opens" &&
            item.MappedTestFilter?.EndsWith("Functional_51_Sell_Gift_Card_Should_Open_Dialog_And_Expose_Numpad", StringComparison.Ordinal) == true);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gaterunner-flaui-matrix-tests-" + Guid.NewGuid().ToString("N"));
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
