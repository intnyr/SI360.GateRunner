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

        SolutionPathBox.Text = settings.SolutionPath;
        TestProjectPathBox.Text = settings.TestProjectPath;
        ResultsDirectoryBox.Text = settings.ResultsDirectory;
        RestoreTimeoutBox.Text = settings.RestoreTimeoutSeconds.ToString();
        BuildTimeoutBox.Text = settings.BuildTimeoutSeconds.ToString();
        GateTimeoutBox.Text = settings.GateTimeoutSeconds.ToString();
        BuildConfigurationBox.Text = settings.BuildConfiguration;
        DeploymentMetadataPathBox.Text = settings.DeploymentMetadataPath;
        ProbeModeBox.Text = settings.ProbeMode;
        ProbeTimeoutBox.Text = settings.ProbeTimeoutSeconds.ToString();
        RetentionDaysBox.Text = settings.ReportRetentionDays.ToString();
        SupportBundleOutputPathBox.Text = settings.SupportBundleOutputPath;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ValidationText.Text = string.Empty;
        if (!ValidatePositiveInt(RestoreTimeoutBox.Text, out var restoreTimeout) ||
            !ValidatePositiveInt(BuildTimeoutBox.Text, out var buildTimeout) ||
            !ValidatePositiveInt(GateTimeoutBox.Text, out var gateTimeout) ||
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
        _settings.ResultsDirectory = ResultsDirectoryBox.Text.Trim();
        _settings.RestoreTimeoutSeconds = restoreTimeout;
        _settings.BuildTimeoutSeconds = buildTimeout;
        _settings.GateTimeoutSeconds = gateTimeout;
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

    private static bool IsValidProbeMode(string value) =>
        string.Equals(value, "Disabled", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "ReadOnly", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "Active", StringComparison.OrdinalIgnoreCase);
}
