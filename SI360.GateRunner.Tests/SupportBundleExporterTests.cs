using System.IO.Compression;
using SI360.GateRunner.Models;
using SI360.GateRunner.Services;

namespace SI360.GateRunner.Tests;

public sealed class SupportBundleExporterTests
{
    [Fact]
    public void Export_PackagesReportsArtifactsMetadataAndProbes()
    {
        using var dir = new TempDirectory();
        var runDir = Path.Combine(dir.Path, "GateRun_20260501_000000Z");
        Directory.CreateDirectory(runDir);
        File.WriteAllText(Path.Combine(runDir, "probe.stdout.log"), "token=[REDACTED]");
        var report = Path.Combine(dir.Path, "GateRun_20260501_000000Z.json");
        File.WriteAllText(report, "{\"schemaVersion\":\"2.1\"}");
        var summary = new RunSummary
        {
            StartedAt = DateTime.UtcNow,
            ReportJsonPath = report,
            Environment = { ArtifactDirectory = runDir },
            DeploymentMetadata = new DeploymentMetadataValidationResult { Metadata = new DeploymentMetadata { SiteId = "SITE-001" } }
        };
        summary.SyntheticProbes.Add(new SyntheticProbeResult("probe", "Probe", "https://example.test", "phase-1-readonly", SyntheticProbeStatus.Passed, 1, "OK"));
        var settings = new RunnerSettings { ResultsDirectory = dir.Path };

        var path = new SupportBundleExporter().Export(summary, settings);

        using var zip = ZipFile.OpenRead(path);
        Assert.Contains(zip.Entries, e => e.FullName == "reports/GateRun_20260501_000000Z.json");
        Assert.Contains(zip.Entries, e => e.FullName == "artifacts/probe.stdout.log");
        Assert.Contains(zip.Entries, e => e.FullName == "metadata-validation.json");
        Assert.Contains(zip.Entries, e => e.FullName == "synthetic-probes.json");
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gaterunner-bundle-tests-{Guid.NewGuid():N}");
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
