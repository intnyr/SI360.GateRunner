using SI360.GateRunner.Services;

namespace SI360.GateRunner.Tests;

public sealed class ReportRetentionPrunerTests
{
    [Fact]
    public void Prune_DeletesOnlyExpiredGateRunEntries()
    {
        using var dir = new TempDirectory();
        var oldJson = Path.Combine(dir.Path, "GateRun_20260331_000000Z.json");
        var currentJson = Path.Combine(dir.Path, "GateRun_20260429_000000Z.json");
        var oldRunDir = Path.Combine(dir.Path, "GateRun_20260331_000000Z");
        File.WriteAllText(oldJson, "{}");
        File.WriteAllText(currentJson, "{}");
        Directory.CreateDirectory(oldRunDir);
        File.WriteAllText(Path.Combine(oldRunDir, "artifact.txt"), "old");

        File.SetLastWriteTimeUtc(oldJson, new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(currentJson, new DateTime(2026, 4, 29, 0, 0, 0, DateTimeKind.Utc));
        Directory.SetLastWriteTimeUtc(oldRunDir, new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc));

        var deleted = ReportRetentionPruner.Prune(dir.Path, 30, new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(2, deleted);
        Assert.False(File.Exists(oldJson));
        Assert.False(Directory.Exists(oldRunDir));
        Assert.True(File.Exists(currentJson));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gaterunner-retention-tests-{Guid.NewGuid():N}");
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
