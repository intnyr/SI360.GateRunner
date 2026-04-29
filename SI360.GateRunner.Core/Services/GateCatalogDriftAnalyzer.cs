using System.IO;
using System.Text.RegularExpressions;
using SI360.GateRunner.Models;

namespace SI360.GateRunner.Services;

public interface IGateDiscoveryService
{
    IReadOnlyList<DiscoveredGate> Discover(RunnerSettings settings);
    IReadOnlyList<GateCatalogWarning> Validate(RunnerSettings settings, IReadOnlyList<GateDefinition>? catalog = null);
}

public sealed partial class GateCatalogDriftAnalyzer : IGateDiscoveryService
{
    [GeneratedRegex(@"\bclass\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled)]
    private static partial Regex ClassRegex();

    [GeneratedRegex(@"^\s*\[(?:Fact|Theory)\b", RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex TestAttributeRegex();

    public IReadOnlyList<DiscoveredGate> Discover(RunnerSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.TestProjectPath))
            return Array.Empty<DiscoveredGate>();

        var projectDir = Path.GetDirectoryName(settings.TestProjectPath);
        if (string.IsNullOrWhiteSpace(projectDir))
            return Array.Empty<DiscoveredGate>();

        return DiscoverFromDirectory(Path.Combine(projectDir, "PreDeploymentGate"));
    }

    public IReadOnlyList<DiscoveredGate> DiscoverFromDirectory(string preDeploymentGateDirectory)
    {
        if (!Directory.Exists(preDeploymentGateDirectory))
            return Array.Empty<DiscoveredGate>();

        var gates = new List<DiscoveredGate>();
        foreach (var file in Directory.EnumerateFiles(preDeploymentGateDirectory, "*Tests.cs"))
        {
            var text = File.ReadAllText(file);
            var classMatch = ClassRegex().Match(text);
            if (!classMatch.Success)
                continue;

            var className = classMatch.Groups["name"].Value;
            if (!className.EndsWith("Tests", StringComparison.Ordinal))
                continue;

            var id = className[..^"Tests".Length];
            var testCount = TestAttributeRegex().Matches(text).Count;
            gates.Add(new DiscoveredGate(id, className, file, testCount));
        }

        return gates
            .OrderBy(g => g.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<GateCatalogWarning> Validate(
        RunnerSettings settings,
        IReadOnlyList<GateDefinition>? catalog = null)
    {
        var projectDir = Path.GetDirectoryName(settings.TestProjectPath);
        var gateDir = string.IsNullOrWhiteSpace(projectDir)
            ? string.Empty
            : Path.Combine(projectDir, "PreDeploymentGate");

        if (string.IsNullOrWhiteSpace(gateDir) || !Directory.Exists(gateDir))
        {
            return new[]
            {
                new GateCatalogWarning(
                    "GATE_DISCOVERY_UNAVAILABLE",
                    $"PreDeploymentGate source directory was not found for test project '{settings.TestProjectPath}'.")
            };
        }

        return Validate(DiscoverFromDirectory(gateDir), catalog ?? GateCatalog.All);
    }

    public IReadOnlyList<GateCatalogWarning> Validate(
        IReadOnlyList<DiscoveredGate> discovered,
        IReadOnlyList<GateDefinition> catalog)
    {
        var warnings = new List<GateCatalogWarning>();
        var discoveredById = discovered.ToDictionary(g => g.Id, StringComparer.OrdinalIgnoreCase);
        var catalogById = catalog.ToDictionary(g => g.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var expected in catalog)
        {
            if (!discoveredById.TryGetValue(expected.Id, out var actual))
            {
                warnings.Add(new GateCatalogWarning(
                    "GATE_MISSING_FROM_SOURCE",
                    $"Catalog gate '{expected.Id}' was not found in SI360.Tests/PreDeploymentGate."));
                continue;
            }

            if (actual.TestCount != expected.ExpectedTestCount)
            {
                warnings.Add(new GateCatalogWarning(
                    "GATE_TEST_COUNT_DRIFT",
                    $"Catalog gate '{expected.Id}' expects {expected.ExpectedTestCount} test(s), but source discovery found {actual.TestCount}."));
            }
        }

        foreach (var actual in discovered)
        {
            if (!catalogById.ContainsKey(actual.Id))
            {
                warnings.Add(new GateCatalogWarning(
                    "GATE_MISSING_FROM_CATALOG",
                    $"Discovered gate '{actual.Id}' in source, but GateRunner catalog has no matching entry."));
            }
        }

        return warnings;
    }
}

public sealed record DiscoveredGate(
    string Id,
    string ClassName,
    string FilePath,
    int TestCount);
