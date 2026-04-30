using System.IO;
using System.Text.Json;

namespace SI360.GateRunner.Services;

public sealed class RunnerSettings
{
    public string SolutionPath { get; set; } = string.Empty;
    public string TestProjectPath { get; set; } = string.Empty;
    public string UiTestProjectPath { get; set; } = string.Empty;
    public string UiAppPath { get; set; } = string.Empty;
    public string UiArtifactsDirectory { get; set; } = string.Empty;
    public string UiValidPin { get; set; } = string.Empty;
    public string UiTestResident { get; set; } = string.Empty;
    public string ResultsDirectory { get; set; } = string.Empty;
    public int PerTestTimeoutSeconds { get; set; } = 60;

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
        var s = new RunnerSettings();
        var candidates = new[]
        {
            AppContext.BaseDirectory,
            Path.Combine(AppContext.BaseDirectory, ".."),
            Path.Combine(AppContext.BaseDirectory, "..", ".."),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "SI360-WPF"),
            @"D:\SI36020WPF",
            @"D:\SI360"
        };

        foreach (var root in candidates)
        {
            var sln = ProbeUp(root, "SI360.slnx", 6);
            if (sln is not null)
            {
                s.SolutionPath = sln;
                var dir = Path.GetDirectoryName(sln)!;
                var csproj = Path.Combine(dir, "SI360.Tests", "SI360.Tests.csproj");
                if (File.Exists(csproj)) s.TestProjectPath = csproj;
                var uiTestProject = Path.Combine(dir, "SI360.UITests", "SI360.UITests.csproj");
                if (File.Exists(uiTestProject)) s.UiTestProjectPath = uiTestProject;
                var releaseUiApp = Path.Combine(dir, "SI360.UI", "bin", "Release", "net8.0-windows", "SI360.UI.exe");
                var debugUiApp = Path.Combine(dir, "SI360.UI", "bin", "Debug", "net8.0-windows", "SI360.UI.exe");
                if (File.Exists(releaseUiApp)) s.UiAppPath = releaseUiApp;
                else if (File.Exists(debugUiApp)) s.UiAppPath = debugUiApp;
                s.UiArtifactsDirectory = Path.Combine(dir, "TestResults", "UIArtifacts");
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
