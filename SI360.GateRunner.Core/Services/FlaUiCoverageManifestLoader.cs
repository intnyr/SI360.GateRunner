using System.Text.Json;
using System.Text.Json.Serialization;
using SI360.GateRunner.Models;

namespace SI360.GateRunner.Services;

public interface IFlaUiCoverageManifestLoader
{
    string ResolveDefaultManifestPath();
    FlaUiCoverageValidationResult Load(string? manifestPath, RunnerSettings settings);
}

public sealed class FlaUiCoverageManifestLoader : IFlaUiCoverageManifestLoader
{
    public const string DefaultManifestFileName = "flaui-coverage-manifest.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string ResolveDefaultManifestPath()
    {
        var basePath = Path.Combine(AppContext.BaseDirectory, DefaultManifestFileName);
        if (File.Exists(basePath)) return basePath;

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 6 && current is not null; i++, current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, DefaultManifestFileName);
            if (File.Exists(candidate)) return candidate;
        }

        return basePath;
    }

    public FlaUiCoverageValidationResult Load(string? manifestPath, RunnerSettings settings)
    {
        var path = string.IsNullOrWhiteSpace(manifestPath) ? ResolveDefaultManifestPath() : manifestPath;
        var result = new FlaUiCoverageValidationResult();

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            result.Errors.Add($"FlaUI coverage manifest was not found at '{path}'.");
            return result;
        }

        FlaUiCoverageManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<FlaUiCoverageManifest>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"FlaUI coverage manifest could not be parsed: {ex.Message}");
            return result;
        }

        if (manifest is null)
        {
            result.Errors.Add("FlaUI coverage manifest was empty.");
            return result;
        }

        result.Manifest = manifest;
        Validate(manifest, settings, result.Errors);
        return result;
    }

    private static void Validate(FlaUiCoverageManifest manifest, RunnerSettings settings, List<string> errors)
    {
        if (manifest.Sections.Count == 0)
            errors.Add("FlaUI coverage manifest must contain at least one section.");

        var groups = manifest.Sections.Select(s => s.Group).Distinct().ToHashSet();
        if (!groups.SetEquals(new[]
            {
                FlaUiCoverageGroup.OrderTakingProcedures,
                FlaUiCoverageGroup.UserFunctionsAndDiningRoomScenarios
            }))
        {
            errors.Add("FlaUI coverage manifest must contain exactly ORDER TAKING PROCEDURES and USER FUNCTIONS AND DINING ROOM SCENARIOS sections.");
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in manifest.Sections)
        {
            if (string.IsNullOrWhiteSpace(section.Name))
                errors.Add($"FlaUI coverage section '{section.Group}' is missing a name.");

            if (!string.IsNullOrWhiteSpace(section.SourceDocument) &&
                !File.Exists(ResolveSi360Path(settings, section.SourceDocument)))
            {
                errors.Add($"FlaUI coverage source document was not found: {section.SourceDocument}");
            }

            foreach (var item in section.Items)
            {
                if (item.Group != section.Group)
                    errors.Add($"FlaUI coverage item '{item.Id}' group does not match its section.");
                if (string.IsNullOrWhiteSpace(item.Id))
                    errors.Add($"FlaUI coverage item '{item.Name}' is missing an id.");
                else if (!ids.Add(item.Id))
                    errors.Add($"Duplicate FlaUI coverage item id '{item.Id}'.");
                if (string.IsNullOrWhiteSpace(item.Name))
                    errors.Add($"FlaUI coverage item '{item.Id}' is missing a name.");

                foreach (var sourceFile in item.SourceFiles.Where(p => !string.IsNullOrWhiteSpace(p)))
                {
                    if (!File.Exists(ResolveSi360Path(settings, sourceFile)))
                        errors.Add($"FlaUI coverage item '{item.Id}' source file was not found: {sourceFile}");
                }

                foreach (var filter in item.TestFilters)
                {
                    if (string.IsNullOrWhiteSpace(filter) ||
                        !(filter.Contains("FullyQualifiedName~", StringComparison.OrdinalIgnoreCase) ||
                          filter.Contains('.', StringComparison.Ordinal) ||
                          filter.Contains('_', StringComparison.Ordinal)))
                    {
                        errors.Add($"FlaUI coverage item '{item.Id}' has an invalid test filter '{filter}'.");
                    }
                }
            }
        }
    }

    public static string ResolveSi360Path(RunnerSettings settings, string path)
    {
        if (Path.IsPathRooted(path)) return path;
        var solutionDir = string.IsNullOrWhiteSpace(settings.SolutionPath)
            ? string.Empty
            : Path.GetDirectoryName(settings.SolutionPath) ?? string.Empty;
        return string.IsNullOrWhiteSpace(solutionDir)
            ? path
            : Path.Combine(solutionDir, path.Replace('/', Path.DirectorySeparatorChar));
    }
}
