using System.Reflection;

namespace SI360.GateRunner.Services;

public static class AppVersionInfo
{
    private static readonly Assembly Assembly = typeof(AppVersionInfo).Assembly;

    public static string Version =>
        Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetName().Version?.ToString()
        ?? "unknown";

    public static string FileVersion =>
        Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version
        ?? Version;

    public static string DisplayVersion => $"v{SemanticVersion}";

    public static string SemanticVersion
    {
        get
        {
            var version = Version;
            var metadataIndex = version.IndexOf('+', StringComparison.Ordinal);
            return metadataIndex > 0 ? version[..metadataIndex] : version;
        }
    }
}
