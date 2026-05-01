using SI360.GateRunner.Services;

namespace SI360.GateRunner.Tests;

public sealed class SecretRedactorTests
{
    [Theory]
    [InlineData("api_key=super-secret-token")]
    [InlineData("Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.secret")]
    [InlineData("https://example.test/status?token=super-secret-token")]
    [InlineData("Server=db;Password=super-secret-token;Database=SI360")]
    public void Redact_RemovesKnownSecretPatterns(string input)
    {
        var redacted = SecretRedactor.Instance.Redact(input);

        Assert.DoesNotContain("super-secret-token", redacted);
        Assert.Contains(SecretRedactor.RedactedValue, redacted);
    }

    [Fact]
    public async Task ProcessRunner_RedactsLiveLogsAndArtifacts()
    {
        using var dir = new TempDirectory();
        var messages = new List<string>();
        var runner = new ProcessRunner(SecretRedactor.Instance);
        var command = new ProcessCommand(
            "cmd.exe",
            "/c echo api_key=super-secret-token",
            Environment.CurrentDirectory,
            TimeSpan.FromSeconds(10),
            dir.Path,
            "redaction-test");

        var result = await runner.RunAsync(command, new Progress<string>(messages.Add), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("super-secret-token", result.StdOut);
        Assert.All(messages, message => Assert.DoesNotContain("super-secret-token", message));

        var commandArtifact = await File.ReadAllTextAsync(Path.Combine(dir.Path, "redaction-test.command.txt"));
        var stdoutArtifact = await File.ReadAllTextAsync(Path.Combine(dir.Path, "redaction-test.stdout.log"));
        Assert.DoesNotContain("super-secret-token", commandArtifact);
        Assert.DoesNotContain("super-secret-token", stdoutArtifact);
        Assert.Contains(SecretRedactor.RedactedValue, commandArtifact);
        Assert.Contains(SecretRedactor.RedactedValue, stdoutArtifact);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gaterunner-redaction-tests-{Guid.NewGuid():N}");
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
