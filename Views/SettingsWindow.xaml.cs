using System.IO;
using System.Windows;
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
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ValidationText.Text = string.Empty;
        if (!ValidatePositiveInt(RestoreTimeoutBox.Text, out var restoreTimeout) ||
            !ValidatePositiveInt(BuildTimeoutBox.Text, out var buildTimeout) ||
            !ValidatePositiveInt(GateTimeoutBox.Text, out var gateTimeout))
        {
            ValidationText.Text = "Timeout values must be whole numbers greater than zero.";
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

        _settings.SolutionPath = SolutionPathBox.Text.Trim();
        _settings.TestProjectPath = TestProjectPathBox.Text.Trim();
        _settings.ResultsDirectory = ResultsDirectoryBox.Text.Trim();
        _settings.RestoreTimeoutSeconds = restoreTimeout;
        _settings.BuildTimeoutSeconds = buildTimeout;
        _settings.GateTimeoutSeconds = gateTimeout;
        DialogResult = true;
    }

    private static bool ValidatePositiveInt(string value, out int parsed) =>
        int.TryParse(value, out parsed) && parsed > 0;
}
