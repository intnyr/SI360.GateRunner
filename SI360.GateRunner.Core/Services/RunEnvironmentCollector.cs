namespace SI360.GateRunner.Services;

using SI360.GateRunner.Models;

public static class RunEnvironmentCollector
{
    public static async Task<RunEnvironment> CollectAsync(
        RunnerSettings settings,
        IProcessRunner processRunner,
        string artifactDirectory,
        CancellationToken cancellationToken)
    {
        var repoPath = Path.GetDirectoryName(settings.SolutionPath) ?? Environment.CurrentDirectory;
        var env = new RunEnvironment
        {
            RepositoryPath = repoPath,
            ArtifactDirectory = artifactDirectory,
            Configuration = settings.ToConfigurationSummary()
        };

        env.DotnetSdkVersion = (await CaptureAsync(processRunner, "dotnet", "--version", repoPath, artifactDirectory, "dotnet-version", cancellationToken)
            .ConfigureAwait(false)).Trim();
        env.Branch = (await CaptureAsync(processRunner, "git", "branch --show-current", repoPath, artifactDirectory, "git-branch", cancellationToken)
            .ConfigureAwait(false)).Trim();
        env.Commit = (await CaptureAsync(processRunner, "git", "rev-parse HEAD", repoPath, artifactDirectory, "git-commit", cancellationToken)
            .ConfigureAwait(false)).Trim();

        env.Commands.Add(GateRunnerCommands.Snapshot("restore", GateRunnerCommands.Restore(settings, artifactDirectory)));
        env.Commands.Add(GateRunnerCommands.Snapshot("build", GateRunnerCommands.Build(settings, artifactDirectory)));
        env.Commands.Add(GateRunnerCommands.GateSnapshot(settings, artifactDirectory));

        return env;
    }

    private static async Task<string> CaptureAsync(
        IProcessRunner processRunner,
        string fileName,
        string arguments,
        string workingDirectory,
        string artifactDirectory,
        string artifactName,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await processRunner.RunAsync(
                new ProcessCommand(
                    fileName,
                    arguments,
                    workingDirectory,
                    TimeSpan.FromSeconds(15),
                    artifactDirectory,
                    artifactName),
                log: null,
                cancellationToken)
                .ConfigureAwait(false);

            return result.ExitCode == 0 ? result.StdOut : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
