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

        var settings = RunnerSettings.Discover(new[] { dir.Path });

        Assert.Equal(Path.Combine(dir.Path, "SI360.slnx"), settings.SolutionPath);
        Assert.Equal(Path.Combine(testsDir, "SI360.Tests.csproj"), settings.TestProjectPath);
        Assert.Equal(Path.Combine(dir.Path, "TestResults"), settings.ResultsDirectory);
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
