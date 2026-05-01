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
            $"build \"{Require(settings.SolutionPath, nameof(settings.SolutionPath))}\" -c {Require(settings.BuildConfiguration, nameof(settings.BuildConfiguration))} --no-restore -p:GenerateFullPaths=true -nologo -clp:ErrorsOnly",
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
