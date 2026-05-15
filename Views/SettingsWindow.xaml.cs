using System.IO;
using System.Windows;
using System.Windows.Controls;
using SI360.GateRunner.Services;

namespace SI360.GateRunner.Views;

public partial class SettingsWindow : Window
{
    private readonly RunnerSettings _settings;

    public SettingsWindow(RunnerSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        Populate(settings, useDetectedFallbacks: true);
    }

    private void UseDetectedValues_Click(object sender, RoutedEventArgs e)
    {
        Populate(RunnerSettings.Discover(), useDetectedFallbacks: true);
        if (!string.IsNullOrWhiteSpace(_settings.Si360UiValidPin))
            Si360UiValidPinBox.Password = _settings.Si360UiValidPin;
        ValidationText.Text = "Detected values loaded. Review and save to persist them.";
    }

    private void Populate(RunnerSettings source, bool useDetectedFallbacks)
    {
        var detected = useDetectedFallbacks ? RunnerSettings.Discover() : new RunnerSettings();
        var solutionPath = FirstExistingFile(source.SolutionPath, detected.SolutionPath);
        var solutionDir = string.IsNullOrWhiteSpace(solutionPath)
            ? RunnerSettings.DefaultSi360Root
            : Path.GetDirectoryName(solutionPath) ?? RunnerSettings.DefaultSi360Root;

        SolutionPathBox.Text = FirstNonEmpty(solutionPath, Path.Combine(RunnerSettings.DefaultSi360Root, "SI360.slnx"));
        TestProjectPathBox.Text = FirstExistingFile(
            source.TestProjectPath,
            detected.TestProjectPath,
            Path.Combine(solutionDir, "SI360.Tests", "SI360.Tests.csproj"));
        FlaUiTestProjectPathBox.Text = FirstExistingFile(
            source.FlaUiTestProjectPath,
            source.ResolveFlaUiTestProjectPath(),
            detected.FlaUiTestProjectPath,
            Path.Combine(solutionDir, "SI360.UITests", "SI360.UITests.csproj"));
        Si360UiAppPathBox.Text = FirstExistingFile(
            source.Si360UiAppPath,
            source.ResolveSi360UiAppPath(),
            detected.Si360UiAppPath,
            Path.Combine(solutionDir, "SI360.UI", "bin", source.BuildConfiguration, "net8.0-windows", "SI360.UI.exe"),
            Path.Combine(solutionDir, "SI360.UI", "bin", "Debug", "net8.0-windows", "SI360.UI.exe"));
        Si360UiValidPinBox.Password = source.ResolveSi360UiValidPin();
        ResultsDirectoryBox.Text = FirstNonEmpty(
            source.ResultsDirectory,
            detected.ResultsDirectory,
            Path.Combine(solutionDir, "TestResults"));
        RestoreTimeoutBox.Text = PositiveOrDefault(source.RestoreTimeoutSeconds, 300).ToString();
        BuildTimeoutBox.Text = PositiveOrDefault(source.BuildTimeoutSeconds, 600).ToString();
        GateTimeoutBox.Text = PositiveOrDefault(source.GateTimeoutSeconds, 900).ToString();
        PerTestTimeoutBox.Text = PositiveOrDefault(source.PerTestTimeoutSeconds, 60).ToString();
        BuildConfigurationBox.Text = FirstNonEmpty(source.BuildConfiguration, "Release");
        DeploymentMetadataPathBox.Text = FirstExistingFile(
            source.DeploymentMetadataPath,
            Path.Combine(solutionDir, "SI360.UI", "deployment-metadata.json"));
        ProbeModeBox.Text = FirstNonEmpty(source.ProbeMode, "ReadOnly");
        ProbeTimeoutBox.Text = PositiveOrDefault(source.ProbeTimeoutSeconds, 30).ToString();
        RetentionDaysBox.Text = PositiveOrDefault(source.ReportRetentionDays, 30).ToString();
        SupportBundleOutputPathBox.Text = source.SupportBundleOutputPath;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ValidationText.Text = string.Empty;
        if (!ValidatePositiveInt(RestoreTimeoutBox.Text, out var restoreTimeout) ||
            !ValidatePositiveInt(BuildTimeoutBox.Text, out var buildTimeout) ||
            !ValidatePositiveInt(GateTimeoutBox.Text, out var gateTimeout) ||
            !ValidatePositiveInt(PerTestTimeoutBox.Text, out var perTestTimeout) ||
            !ValidatePositiveInt(ProbeTimeoutBox.Text, out var probeTimeout) ||
            !ValidatePositiveInt(RetentionDaysBox.Text, out var retentionDays))
        {
            ValidationText.Text = "Timeout and retention values must be whole numbers greater than zero.";
            return;
        }

        if (string.IsNullOrWhiteSpace(SolutionPathBox.Text) || !File.Exists(SolutionPathBox.Text))
        {
            ValidationText.Text = "Solution path must point to an existing solution file.";
            return;
        }

        if (string.IsNullOrWhiteSpace(TestProjectPathBox.Text) || !File.Exists(TestProjectPathBox.Text))
        {
            ValidationText.Text = "Test project path must point to an existing project file.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(FlaUiTestProjectPathBox.Text) &&
            !File.Exists(FlaUiTestProjectPathBox.Text))
        {
            ValidationText.Text = "FlaUI test project path must point to an existing project file when provided.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(Si360UiAppPathBox.Text) &&
            !File.Exists(Si360UiAppPathBox.Text))
        {
            ValidationText.Text = "SI360 UI app path must point to an existing executable file when provided.";
            return;
        }

        if (string.IsNullOrWhiteSpace(ResultsDirectoryBox.Text))
        {
            ValidationText.Text = "Results directory is required.";
            return;
        }

        if (string.IsNullOrWhiteSpace(BuildConfigurationBox.Text))
        {
            ValidationText.Text = "Build configuration is required.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(DeploymentMetadataPathBox.Text) &&
            !File.Exists(DeploymentMetadataPathBox.Text))
        {
            ValidationText.Text = "Deployment metadata path must point to an existing file when provided.";
            return;
        }

        var probeMode = ProbeModeBox.Text.Trim();
        if (!IsValidProbeMode(probeMode))
        {
            ValidationText.Text = "Probe mode must be Disabled, ReadOnly, or Active.";
            return;
        }

        _settings.SolutionPath = SolutionPathBox.Text.Trim();
        _settings.TestProjectPath = TestProjectPathBox.Text.Trim();
        _settings.FlaUiTestProjectPath = FlaUiTestProjectPathBox.Text.Trim();
        _settings.Si360UiAppPath = Si360UiAppPathBox.Text.Trim();
        _settings.Si360UiValidPin = Si360UiValidPinBox.Password.Trim();
        _settings.ResultsDirectory = ResultsDirectoryBox.Text.Trim();
        _settings.RestoreTimeoutSeconds = restoreTimeout;
        _settings.BuildTimeoutSeconds = buildTimeout;
        _settings.GateTimeoutSeconds = gateTimeout;
        _settings.PerTestTimeoutSeconds = perTestTimeout;
        _settings.BuildConfiguration = BuildConfigurationBox.Text.Trim();
        _settings.DeploymentMetadataPath = DeploymentMetadataPathBox.Text.Trim();
        _settings.ProbeMode = probeMode;
        _settings.ProbeTimeoutSeconds = probeTimeout;
        _settings.ReportRetentionDays = retentionDays;
        _settings.SupportBundleOutputPath = SupportBundleOutputPathBox.Text.Trim();
        DialogResult = true;
    }

    private static bool ValidatePositiveInt(string value, out int parsed) =>
        int.TryParse(value, out parsed) && parsed > 0;

    private static int PositiveOrDefault(int value, int defaultValue) => value > 0 ? value : defaultValue;

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string FirstExistingFile(params string[] values)
    {
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;
            if (File.Exists(value))
                return value.Trim();
        }

        return FirstNonEmpty(values);
    }

    private static bool IsValidProbeMode(string value) =>
        string.Equals(value, "Disabled", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "ReadOnly", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "Active", StringComparison.OrdinalIgnoreCase);
}
