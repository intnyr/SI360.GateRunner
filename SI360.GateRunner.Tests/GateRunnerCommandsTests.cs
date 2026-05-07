using SI360.GateRunner.Services;

namespace SI360.GateRunner.Tests;

public sealed class GateRunnerCommandsTests
{
    [Fact]
    public async Task RestoreAsync_RestoresSolutionPath()
    {
        var settings = Settings();
        var runner = new DotnetTestRunner(settings, new CaptureProcessRunner());

        await runner.RestoreAsync(null, CancellationToken.None, "artifacts");

        var command = CaptureProcessRunner.LastCommand;
        Assert.NotNull(command);
        Assert.Contains($"restore \"{settings.SolutionPath}\"", command!.Arguments);
        Assert.DoesNotContain(settings.TestProjectPath, command.Arguments);
        Assert.Equal("restore", command.ArtifactName);
    }

    [Fact]
    public async Task BuildAsync_BuildsSolutionWithoutRestore()
    {
        var settings = Settings();
        var runner = new BuildErrorCollector(settings, new CaptureProcessRunner());

        await runner.BuildAsync(null, CancellationToken.None, "artifacts");

        var command = CaptureProcessRunner.LastCommand;
        Assert.NotNull(command);
        Assert.Contains($"build \"{settings.SolutionPath}\"", command!.Arguments);
        Assert.Contains("--no-restore", command.Arguments);
        Assert.DoesNotContain("ErrorsOnly", command.Arguments);
        Assert.Equal("build", command.ArtifactName);
    }

    [Fact]
    public async Task BuildAsync_CapturesWarningsAndErrorsAsQualityIssues()
    {
        var settings = Settings();
        var runner = new BuildErrorCollector(settings, new CaptureProcessRunner(
            "D:\\Repo\\Warn.cs(12,4): warning CS0618: Obsolete member",
            "D:\\Repo\\Error.cs(20,8): error CS1002: ; expected"));

        var (_, issues) = await runner.BuildAsync(null, CancellationToken.None, "artifacts");

        Assert.Equal(2, issues.Count);
        Assert.Contains(issues, i => i.Severity == SI360.GateRunner.Models.QualityIssueSeverity.Warning && i.Code == "CS0618");
        Assert.Contains(issues, i => i.Severity == SI360.GateRunner.Models.QualityIssueSeverity.Error && i.Code == "CS1002");
    }

    [Fact]
    public void FlaUiCoverageCommand_WritesTrxToCoverageRunDirectory()
    {
        var settings = Settings();
        var command = GateRunnerCommands.FlaUiCoverage(
            settings,
            "FullyQualifiedName~Functional_01_Login_To_Room_Should_Succeed",
            "coverage-artifacts",
            "coverage.trx",
            filterCount: 1);

        Assert.Contains($"test \"{settings.FlaUiTestProjectPath}\"", command.Arguments);
        Assert.DoesNotContain($"test \"{settings.TestProjectPath}\"", command.Arguments);
        Assert.NotNull(command.EnvironmentVariables);
        Assert.Equal(settings.Si360UiAppPath, command.EnvironmentVariables!["SI360_UI_APP_PATH"]);
        Assert.Equal(settings.Si360UiValidPin, command.EnvironmentVariables["SI360_UI_VALID_PIN"]);
        Assert.Contains("--filter \"FullyQualifiedName~Functional_01_Login_To_Room_Should_Succeed\"", command.Arguments);
        Assert.Contains("--logger \"trx;LogFileName=coverage.trx\"", command.Arguments);
        Assert.Contains("--results-directory \"coverage-artifacts\"", command.Arguments);
        Assert.Equal("flaui-coverage", command.ArtifactName);
        Assert.Equal("coverage-artifacts", command.ArtifactDirectory);
    }

    [Fact]
    public void Snapshot_MatchesCommandFactoryOutput()
    {
        var settings = Settings();
        var command = GateRunnerCommands.Build(settings, "artifacts");
        var snapshot = GateRunnerCommands.Snapshot("build", command);

        Assert.Equal($"dotnet {command.Arguments}", snapshot.CommandLine);
        Assert.Equal(command.WorkingDirectory, snapshot.WorkingDirectory);
        Assert.Equal(command.Timeout.TotalSeconds, snapshot.TimeoutSeconds);
        Assert.Equal(command.ArtifactDirectory, snapshot.ArtifactDirectory);
        Assert.Equal(command.ArtifactName, snapshot.ArtifactName);
    }

    private static RunnerSettings Settings()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gaterunner-commands-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "SI360.Tests"));
        Directory.CreateDirectory(Path.Combine(root, "SI360.UITests"));
        return new RunnerSettings
        {
            SolutionPath = Path.Combine(root, "SI360.slnx"),
            TestProjectPath = Path.Combine(root, "SI360.Tests", "SI360.Tests.csproj"),
            FlaUiTestProjectPath = Path.Combine(root, "SI360.UITests", "SI360.UITests.csproj"),
            Si360UiAppPath = Path.Combine(root, "SI360.UI", "bin", "Debug", "net8.0-windows", "SI360.UI.exe"),
            Si360UiValidPin = "7458",
            ResultsDirectory = Path.Combine(root, "TestResults"),
            RestoreTimeoutSeconds = 30,
            BuildTimeoutSeconds = 40,
            GateTimeoutSeconds = 50
        };
    }

    private sealed class CaptureProcessRunner : IProcessRunner
    {
        private readonly string[] _lines;

        public CaptureProcessRunner(params string[] lines)
        {
            _lines = lines;
        }

        public static ProcessCommand? LastCommand { get; private set; }

        public Task<ProcessRunResult> RunAsync(
            ProcessCommand command,
            IProgress<string>? log,
            CancellationToken cancellationToken)
        {
            LastCommand = command;
            foreach (var line in _lines)
                log?.Report(line);
            return Task.FromResult(new ProcessRunResult(0, string.Empty, string.Empty, false, false, command.ArtifactDirectory));
        }
    }
}
