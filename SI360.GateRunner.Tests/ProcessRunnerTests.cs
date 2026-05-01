using SI360.GateRunner.Services;

namespace SI360.GateRunner.Tests;

public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_WritesArtifactsForCompletedProcess()
    {
        using var dir = new TempDirectory();
        var runner = new ProcessRunner();

        var result = await runner.RunAsync(
            new ProcessCommand(
                "cmd.exe",
                "/c echo hello",
                Environment.CurrentDirectory,
                TimeSpan.FromSeconds(10),
                dir.Path,
                "echo"),
            log: null,
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello", result.StdOut);
        Assert.True(File.Exists(Path.Combine(dir.Path, "echo.command.txt")));
        Assert.True(File.Exists(Path.Combine(dir.Path, "echo.stdout.log")));
        Assert.True(File.Exists(Path.Combine(dir.Path, "echo.exit.txt")));
    }

    [Fact]
    public async Task RunAsync_ReportsTimeout()
    {
        using var dir = new TempDirectory();
        var runner = new ProcessRunner();

        var result = await runner.RunAsync(
            new ProcessCommand(
                "powershell.exe",
                "-NoProfile -Command Start-Sleep -Seconds 5",
                Environment.CurrentDirectory,
                TimeSpan.FromMilliseconds(250),
                dir.Path,
                "timeout"),
            log: null,
            CancellationToken.None);

        Assert.True(result.TimedOut);
        Assert.NotEqual(0, result.ExitCode);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gaterunner-process-tests-{Guid.NewGuid():N}");
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
