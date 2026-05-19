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

    public static string DisplayVersion => $"v{Version}";
}
