using System.IO;
using System.Text.Json;

namespace SI360.GateRunner.Services;

public sealed class RunnerSettings
{
    public string SolutionPath { get; set; } = string.Empty;
    public string TestProjectPath { get; set; } = string.Empty;
    public string ResultsDirectory { get; set; } = string.Empty;
    public int PerTestTimeoutSeconds { get; set; } = 60;
    public int RestoreTimeoutSeconds { get; set; } = 300;
    public int BuildTimeoutSeconds { get; set; } = 600;
    public int GateTimeoutSeconds { get; set; } = 900;

    private static string SettingsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SI360.GateRunner",
        "settings.json");

    public static RunnerSettings LoadOrDiscover()
    {
        if (File.Exists(SettingsFile))
        {
            try
            {
                var json = File.ReadAllText(SettingsFile);
                var parsed = JsonSerializer.Deserialize<RunnerSettings>(json);
                if (parsed is not null && File.Exists(parsed.SolutionPath)) return parsed;
            }
            catch { }
        }
        return Discover();
    }

    public static RunnerSettings Discover()
    {
        var candidates = new[]
        {
            AppContext.BaseDirectory,
            Path.Combine(AppContext.BaseDirectory, ".."),
            Path.Combine(AppContext.BaseDirectory, "..", ".."),
            @"D:\SI36020WPF",
            @"D:\SI360"
        };

        return Discover(candidates);
    }

    public static RunnerSettings Discover(IEnumerable<string> candidates)
    {
        var s = new RunnerSettings();

        foreach (var root in candidates)
        {
            var sln = ProbeUp(root, "SI360.slnx", 6);
            if (sln is not null)
            {
                s.SolutionPath = sln;
                var dir = Path.GetDirectoryName(sln)!;
                var csproj = Path.Combine(dir, "SI360.Tests", "SI360.Tests.csproj");
                if (File.Exists(csproj)) s.TestProjectPath = csproj;
                s.ResultsDirectory = Path.Combine(dir, "TestResults");
                return s;
            }
        }
        return s;
    }

    private static string? ProbeUp(string start, string fileName, int maxHops)
    {
        var dir = Path.GetFullPath(start);
        for (var i = 0; i < maxHops; i++)
        {
            var candidate = Path.Combine(dir, fileName);
            if (File.Exists(candidate)) return candidate;
            var parent = Path.GetDirectoryName(dir);
            if (parent is null || parent == dir) break;
            dir = parent;
        }
        return null;
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(SettingsFile)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(SettingsFile, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
