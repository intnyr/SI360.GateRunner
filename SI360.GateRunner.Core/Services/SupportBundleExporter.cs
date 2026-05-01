using System.IO.Compression;
using System.Text.Json;
using SI360.GateRunner.Models;

namespace SI360.GateRunner.Services;

public interface ISupportBundleExporter
{
    string Export(RunSummary summary, RunnerSettings settings);
}

public sealed class SupportBundleExporter : ISupportBundleExporter
{
    public string Export(RunSummary summary, RunnerSettings settings)
    {
        var outputPath = string.IsNullOrWhiteSpace(settings.SupportBundleOutputPath)
            ? Path.Combine(settings.ResultsDirectory, $"GateRunnerSupportBundle_{DateTime.UtcNow:yyyyMMdd_HHmmss}Z.zip")
            : settings.SupportBundleOutputPath;

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? settings.ResultsDirectory);
        if (File.Exists(outputPath))
            File.Delete(outputPath);

        using var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create);
        AddIfExists(archive, summary.ReportMarkdownPath, "reports");
        AddIfExists(archive, summary.ReportJsonPath, "reports");

        if (!string.IsNullOrWhiteSpace(summary.Environment.ArtifactDirectory) &&
            Directory.Exists(summary.Environment.ArtifactDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(summary.Environment.ArtifactDirectory, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(summary.Environment.ArtifactDirectory, file).Replace('\\', '/');
                archive.CreateEntryFromFile(file, $"artifacts/{rel}");
            }
        }

        AddJson(archive, "metadata-validation.json", summary.DeploymentMetadata);
        AddJson(archive, "synthetic-probes.json", summary.SyntheticProbes);
        return outputPath;
    }

    private static void AddIfExists(ZipArchive archive, string? path, string prefix)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        archive.CreateEntryFromFile(path, $"{prefix}/{Path.GetFileName(path)}");
    }

    private static void AddJson<T>(ZipArchive archive, string name, T value)
    {
        var entry = archive.CreateEntry(name);
        using var stream = entry.Open();
        JsonSerializer.Serialize(stream, value, new JsonSerializerOptions { WriteIndented = true });
    }
}
