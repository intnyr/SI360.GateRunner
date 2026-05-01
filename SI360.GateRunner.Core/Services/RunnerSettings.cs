using System.IO;
using System.Text.Json;
using SI360.GateRunner.Models;

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
    public string BuildConfiguration { get; set; } = "Release";
    public string DeploymentMetadataPath { get; set; } = string.Empty;
    public string ProbeMode { get; set; } = "ReadOnly";
    public int ProbeTimeoutSeconds { get; set; } = 30;
    public int ReportRetentionDays { get; set; } = 30;
    public string SupportBundleOutputPath { get; set; } = string.Empty;

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

    public void ApplyEnvironmentVariables(Func<string, string?>? getEnvironmentVariable = null)
    {
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;
        ApplyString(nameof(SolutionPath), value => SolutionPath = value);
        ApplyString(nameof(TestProjectPath), value => TestProjectPath = value);
        ApplyString(nameof(ResultsDirectory), value => ResultsDirectory = value);
        ApplyInt(nameof(PerTestTimeoutSeconds), value => PerTestTimeoutSeconds = value);
        ApplyInt(nameof(RestoreTimeoutSeconds), value => RestoreTimeoutSeconds = value);
        ApplyInt(nameof(BuildTimeoutSeconds), value => BuildTimeoutSeconds = value);
        ApplyInt(nameof(GateTimeoutSeconds), value => GateTimeoutSeconds = value);
        ApplyString(nameof(BuildConfiguration), value => BuildConfiguration = value);
        ApplyString(nameof(DeploymentMetadataPath), value => DeploymentMetadataPath = value);
        ApplyString(nameof(ProbeMode), value => ProbeMode = value);
        ApplyInt(nameof(ProbeTimeoutSeconds), value => ProbeTimeoutSeconds = value);
        ApplyInt(nameof(ReportRetentionDays), value => ReportRetentionDays = value);
        ApplyString(nameof(SupportBundleOutputPath), value => SupportBundleOutputPath = value);

        void ApplyString(string propertyName, Action<string> apply)
        {
            var value = getEnvironmentVariable($"GATERUNNER_{propertyName}");
            if (!string.IsNullOrWhiteSpace(value))
                apply(value.Trim());
        }

        void ApplyInt(string propertyName, Action<int> apply)
        {
            var value = getEnvironmentVariable($"GATERUNNER_{propertyName}");
            if (int.TryParse(value, out var parsed))
                apply(parsed);
        }
    }

    public List<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(SolutionPath) || !File.Exists(SolutionPath))
            errors.Add("SolutionPath must point to an existing solution file.");
        if (string.IsNullOrWhiteSpace(TestProjectPath) || !File.Exists(TestProjectPath))
            errors.Add("TestProjectPath must point to an existing project file.");
        if (string.IsNullOrWhiteSpace(ResultsDirectory))
            errors.Add("ResultsDirectory is required.");
        if (RestoreTimeoutSeconds <= 0)
            errors.Add("RestoreTimeoutSeconds must be greater than zero.");
        if (BuildTimeoutSeconds <= 0)
            errors.Add("BuildTimeoutSeconds must be greater than zero.");
        if (GateTimeoutSeconds <= 0)
            errors.Add("GateTimeoutSeconds must be greater than zero.");
        if (ProbeTimeoutSeconds <= 0)
            errors.Add("ProbeTimeoutSeconds must be greater than zero.");
        if (ReportRetentionDays <= 0)
            errors.Add("ReportRetentionDays must be greater than zero.");
        if (string.IsNullOrWhiteSpace(BuildConfiguration))
            errors.Add("BuildConfiguration is required.");
        if (!IsValidProbeMode(ProbeMode))
            errors.Add("ProbeMode must be Disabled, ReadOnly, or Active.");
        if (!string.IsNullOrWhiteSpace(DeploymentMetadataPath) && !File.Exists(DeploymentMetadataPath))
            errors.Add("DeploymentMetadataPath must point to an existing file when provided.");
        return errors;
    }

    public RunnerConfigurationSummary ToConfigurationSummary() => new(
        BuildConfiguration,
        string.IsNullOrWhiteSpace(DeploymentMetadataPath) ? null : DeploymentMetadataPath,
        ProbeMode,
        ProbeTimeoutSeconds,
        ReportRetentionDays,
        string.IsNullOrWhiteSpace(SupportBundleOutputPath) ? null : SupportBundleOutputPath);

    private static bool IsValidProbeMode(string value) =>
        string.Equals(value, "Disabled", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "ReadOnly", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "Active", StringComparison.OrdinalIgnoreCase);
}
