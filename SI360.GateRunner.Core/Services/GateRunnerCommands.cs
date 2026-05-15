using SI360.GateRunner.Models;

namespace SI360.GateRunner.Services;

public static class GateRunnerCommands
{
    public static ProcessCommand Restore(RunnerSettings settings, string? artifactDirectory) =>
        new(
            "dotnet",
            $"restore \"{Require(settings.SolutionPath, nameof(settings.SolutionPath))}\" -nologo",
            WorkingDirectoryFor(settings.SolutionPath),
            TimeSpan.FromSeconds(Math.Max(1, settings.RestoreTimeoutSeconds)),
            artifactDirectory,
            "restore");

    public static ProcessCommand Build(RunnerSettings settings, string? artifactDirectory) =>
        new(
            "dotnet",
            $"build \"{Require(settings.SolutionPath, nameof(settings.SolutionPath))}\" -c {Require(settings.BuildConfiguration, nameof(settings.BuildConfiguration))} --no-restore -p:GenerateFullPaths=true -nologo -clp:Summary",
            WorkingDirectoryFor(settings.SolutionPath),
            TimeSpan.FromSeconds(Math.Max(1, settings.BuildTimeoutSeconds)),
            artifactDirectory,
            "build");

    public static ProcessCommand Gate(
        RunnerSettings settings,
        string gateId,
        string filter,
        string runDirectory,
        string trxName) =>
        new(
            "dotnet",
            $"test \"{Require(settings.TestProjectPath, nameof(settings.TestProjectPath))}\" --no-build --nologo --filter \"{filter}\" --logger \"trx;LogFileName={trxName}\" --results-directory \"{runDirectory}\" -v normal",
            WorkingDirectoryFor(settings.TestProjectPath),
            TimeSpan.FromSeconds(Math.Max(1, settings.GateTimeoutSeconds)),
            runDirectory,
            $"gate-{gateId}");

    public static ProcessCommand FlaUiTest(
        RunnerSettings settings,
        string filter,
        string runDirectory,
        string trxName,
        int filterCount)
    {
        var testProjectPath = Require(settings.ResolveFlaUiTestProjectPath(), nameof(settings.FlaUiTestProjectPath));
        var appPath = Require(settings.ResolveSi360UiAppPath(), nameof(settings.Si360UiAppPath));
        var scenarioTimeoutSeconds = Math.Max(180, Math.Max(1, settings.PerTestTimeoutSeconds) * Math.Max(1, filterCount));
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SI360_UI_APP_PATH"] = appPath
        };
        AddWindowsDirectoryEnvironment(environment);
        var validPin = settings.ResolveSi360UiValidPin();
        if (!string.IsNullOrWhiteSpace(validPin))
            environment["SI360_UI_VALID_PIN"] = validPin;

        return new(
            "dotnet",
            $"test \"{testProjectPath}\" --no-build --nologo --filter \"{filter}\" --logger \"trx;LogFileName={trxName}\" --results-directory \"{runDirectory}\" -v normal",
            WorkingDirectoryFor(testProjectPath),
            TimeSpan.FromSeconds(scenarioTimeoutSeconds),
            runDirectory,
            "flaui-test",
            environment);
    }

    private static void AddWindowsDirectoryEnvironment(IDictionary<string, string> environment)
    {
        var windowsDirectory = Environment.GetEnvironmentVariable("windir");
        if (string.IsNullOrWhiteSpace(windowsDirectory))
            windowsDirectory = Environment.GetEnvironmentVariable("SystemRoot");
        if (string.IsNullOrWhiteSpace(windowsDirectory))
            windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        if (string.IsNullOrWhiteSpace(windowsDirectory))
            return;

        environment["windir"] = windowsDirectory;
        environment["SystemRoot"] = windowsDirectory;
    }

    public static ProcessCommandSnapshot Snapshot(string name, ProcessCommand command) =>
        new(
            name,
            $"{command.FileName} {command.Arguments}",
            command.WorkingDirectory,
            command.Timeout.TotalSeconds,
            command.ArtifactDirectory,
            command.ArtifactName);

    public static ProcessCommandSnapshot GateSnapshot(RunnerSettings settings, string artifactDirectory) =>
        Snapshot(
            "gate",
            new ProcessCommand(
                "dotnet",
                $"test \"{Require(settings.TestProjectPath, nameof(settings.TestProjectPath))}\" --no-build --nologo --filter <gate> --logger \"trx;LogFileName=<gate>.trx\" --results-directory \"{artifactDirectory}\" -v normal",
                WorkingDirectoryFor(settings.TestProjectPath),
                TimeSpan.FromSeconds(Math.Max(1, settings.GateTimeoutSeconds)),
                artifactDirectory,
                "gate-<gate>"));

    private static string Require(string value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{name} not configured.")
            : value;

    private static string WorkingDirectoryFor(string path) =>
        Path.GetDirectoryName(path) ?? Environment.CurrentDirectory;
}
