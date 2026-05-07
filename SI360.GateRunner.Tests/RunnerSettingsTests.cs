using SI360.GateRunner.Services;

namespace SI360.GateRunner.Tests;

public sealed class RunnerSettingsTests
{
    [Fact]
    public void Discover_FindsSolutionTestProjectAndResultsDirectoryFromCandidateRoot()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "SI360.slnx"), string.Empty);
        var testsDir = Path.Combine(dir.Path, "SI360.Tests");
        Directory.CreateDirectory(testsDir);
        File.WriteAllText(Path.Combine(testsDir, "SI360.Tests.csproj"), "<Project />");
        var uiTestsDir = Path.Combine(dir.Path, "SI360.UITests");
        Directory.CreateDirectory(uiTestsDir);
        File.WriteAllText(Path.Combine(uiTestsDir, "SI360.UITests.csproj"), "<Project />");
        var uiBinDir = Path.Combine(dir.Path, "SI360.UI", "bin", "Release", "net8.0-windows");
        Directory.CreateDirectory(uiBinDir);
        File.WriteAllText(Path.Combine(uiBinDir, "SI360.UI.exe"), string.Empty);

        var settings = RunnerSettings.Discover(new[] { dir.Path });

        Assert.Equal(Path.Combine(dir.Path, "SI360.slnx"), settings.SolutionPath);
        Assert.Equal(Path.Combine(testsDir, "SI360.Tests.csproj"), settings.TestProjectPath);
        Assert.Equal(Path.Combine(uiTestsDir, "SI360.UITests.csproj"), settings.FlaUiTestProjectPath);
        Assert.Equal(Path.Combine(uiBinDir, "SI360.UI.exe"), settings.Si360UiAppPath);
        Assert.Equal(Path.Combine(dir.Path, "TestResults"), settings.ResultsDirectory);
    }

    [Fact]
    public void DefaultSi360Root_PointsToEDriveApplication()
    {
        Assert.Equal(@"E:\SI36020WPF", RunnerSettings.DefaultSi360Root);
    }

    [Fact]
    public void ApplyEnvironmentVariables_OverridesNewConfigurationFields()
    {
        var settings = new RunnerSettings();
        var values = new Dictionary<string, string?>
        {
            ["GATERUNNER_BuildConfiguration"] = "Debug",
            ["GATERUNNER_DeploymentMetadataPath"] = @"D:\metadata.json",
            ["GATERUNNER_ProbeMode"] = "Active",
            ["GATERUNNER_ProbeTimeoutSeconds"] = "45",
            ["GATERUNNER_ReportRetentionDays"] = "14",
            ["GATERUNNER_SupportBundleOutputPath"] = @"D:\bundle.zip",
            ["GATERUNNER_FlaUiTestProjectPath"] = @"D:\SI360.UITests\SI360.UITests.csproj",
            ["GATERUNNER_Si360UiAppPath"] = @"D:\SI360.UI\SI360.UI.exe",
            ["GATERUNNER_Si360UiValidPin"] = "7458"
        };

        settings.ApplyEnvironmentVariables(key => values.TryGetValue(key, out var value) ? value : null);

        Assert.Equal("Debug", settings.BuildConfiguration);
        Assert.Equal(@"D:\metadata.json", settings.DeploymentMetadataPath);
        Assert.Equal("Active", settings.ProbeMode);
        Assert.Equal(45, settings.ProbeTimeoutSeconds);
        Assert.Equal(14, settings.ReportRetentionDays);
        Assert.Equal(@"D:\bundle.zip", settings.SupportBundleOutputPath);
        Assert.Equal(@"D:\SI360.UITests\SI360.UITests.csproj", settings.FlaUiTestProjectPath);
        Assert.Equal(@"D:\SI360.UI\SI360.UI.exe", settings.Si360UiAppPath);
        Assert.Equal("7458", settings.Si360UiValidPin);
    }

    [Fact]
    public void Validate_ReturnsActionableErrorsForInvalidConfiguration()
    {
        var settings = new RunnerSettings
        {
            SolutionPath = string.Empty,
            TestProjectPath = string.Empty,
            ResultsDirectory = string.Empty,
            BuildConfiguration = string.Empty,
            ProbeMode = "WriteAll",
            ProbeTimeoutSeconds = 0,
            ReportRetentionDays = 0
        };

        var errors = settings.Validate();

        Assert.Contains(errors, e => e.Contains(nameof(RunnerSettings.SolutionPath), StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains(nameof(RunnerSettings.TestProjectPath), StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains(nameof(RunnerSettings.ResultsDirectory), StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains(nameof(RunnerSettings.BuildConfiguration), StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains(nameof(RunnerSettings.ProbeMode), StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains(nameof(RunnerSettings.ProbeTimeoutSeconds), StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains(nameof(RunnerSettings.ReportRetentionDays), StringComparison.Ordinal));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gaterunner-settings-tests-{Guid.NewGuid():N}");
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
