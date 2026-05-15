using System.IO;
using SI360.GateRunner.Models;

namespace SI360.GateRunner.Services;

public interface ITestScenarioMatrixLoader
{
    TestScenarioMatrixDocument Load(RunnerSettings settings, string? sourcePath = null);
}

public sealed class TestScenarioMatrixLoader : ITestScenarioMatrixLoader
{
    public const string DefaultDocumentRelativePath = @"DOCS\Unit-Integration-Service-Test-Scenario-Matrix-2026-05-15.md";

    public TestScenarioMatrixDocument Load(RunnerSettings settings, string? sourcePath = null)
    {
        var path = ResolvePath(settings, sourcePath);
        var document = new TestScenarioMatrixDocument
        {
            SourcePath = path,
            LoadedAt = DateTime.UtcNow
        };

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            document.LoadErrors.Add($"Test scenario matrix document was not found at '{path}'.");
            return document;
        }

        try
        {
            var lines = File.ReadAllLines(path);
            ParseScenarioRows(lines, document);
            MapExistingTests(settings, document);
            if (document.Items.Count == 0)
                document.LoadErrors.Add("Test scenario matrix document did not contain any scenario rows.");
        }
        catch (Exception ex)
        {
            document.LoadErrors.Add($"Test scenario matrix document could not be loaded: {ex.Message}");
        }

        return document;
    }

    public static string ResolvePath(RunnerSettings settings, string? sourcePath = null)
    {
        if (!string.IsNullOrWhiteSpace(sourcePath))
            return Path.IsPathRooted(sourcePath) ? sourcePath : ResolveFromSolutionRoot(settings, sourcePath);

        return ResolveFromSolutionRoot(settings, DefaultDocumentRelativePath);
    }

    private static string ResolveFromSolutionRoot(RunnerSettings settings, string relativePath)
    {
        var solutionDir = string.IsNullOrWhiteSpace(settings.SolutionPath)
            ? RunnerSettings.DefaultSi360Root
            : Path.GetDirectoryName(settings.SolutionPath) ?? RunnerSettings.DefaultSi360Root;
        return Path.GetFullPath(Path.Combine(solutionDir, relativePath));
    }

    private static void ParseScenarioRows(IEnumerable<string> lines, TestScenarioMatrixDocument document)
    {
        var inScenarioTable = false;
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.StartsWith("## Scenario Matrix", StringComparison.OrdinalIgnoreCase))
            {
                inScenarioTable = true;
                continue;
            }

            if (!inScenarioTable)
                continue;

            if (line.StartsWith("## ", StringComparison.OrdinalIgnoreCase))
                break;

            if (!line.StartsWith('|') || !line.EndsWith('|'))
                continue;

            if (line.Contains("|---", StringComparison.Ordinal))
                continue;

            var cells = SplitMarkdownRow(line);
            if (cells.Count != 7)
                continue;

            if (cells[0].Equals("Area", StringComparison.OrdinalIgnoreCase))
                continue;

            document.Items.Add(new TestScenarioMatrixItem
            {
                Area = cells[0],
                Scenario = cells[1],
                RecommendedTestType = cells[2],
                SetupInputs = cells[3],
                ExpectedResult = cells[4],
                SuggestedTargetCode = cells[5],
                Notes = cells[6]
            });
        }
    }

    private static List<string> SplitMarkdownRow(string line)
    {
        return line.Trim()
            .Trim('|')
            .Split('|')
            .Select(cell => cell.Trim())
            .Select(TrimCodeTicks)
            .ToList();
    }

    private static string TrimCodeTicks(string value)
        => value.Trim().Replace("`", string.Empty, StringComparison.Ordinal);

    private static void MapExistingTests(RunnerSettings settings, TestScenarioMatrixDocument document)
    {
        var testRoot = ResolveTestRoot(settings);
        if (string.IsNullOrWhiteSpace(testRoot) || !Directory.Exists(testRoot))
        {
            document.LoadErrors.Add($"SI360 test source folder was not found at '{testRoot}'.");
            return;
        }

        var methods = DiscoverTestMethods(testRoot);
        foreach (var item in document.Items)
        {
            var match = methods.FirstOrDefault(method =>
                string.Equals(method.Name, item.Scenario, StringComparison.OrdinalIgnoreCase));
            if (match is null)
                continue;

            item.MappedTestFilter = match.FullyQualifiedName;
            item.MappedTestSource = match.SourcePath;
        }
    }

    private static string ResolveTestRoot(RunnerSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.TestProjectPath) && File.Exists(settings.TestProjectPath))
            return Path.GetDirectoryName(settings.TestProjectPath) ?? string.Empty;

        var solutionDir = string.IsNullOrWhiteSpace(settings.SolutionPath)
            ? RunnerSettings.DefaultSi360Root
            : Path.GetDirectoryName(settings.SolutionPath) ?? RunnerSettings.DefaultSi360Root;
        return Path.Combine(solutionDir, "SI360.Tests");
    }

    private static List<DiscoveredTestMethod> DiscoverTestMethods(string testRoot)
    {
        var result = new List<DiscoveredTestMethod>();
        foreach (var file in Directory.EnumerateFiles(testRoot, "*.cs", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(file);
            var pendingTestAttribute = false;
            var namespaceName = string.Empty;
            var currentClass = string.Empty;

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (line.StartsWith("namespace ", StringComparison.Ordinal))
                {
                    namespaceName = line["namespace ".Length..].Trim().TrimEnd(';', '{').Trim();
                }

                var className = TryParseClassName(line);
                if (!string.IsNullOrWhiteSpace(className))
                {
                    currentClass = className;
                }

                if (line.Contains("[Fact", StringComparison.Ordinal) ||
                    line.Contains("[Theory", StringComparison.Ordinal))
                {
                    pendingTestAttribute = true;
                }

                if (pendingTestAttribute)
                {
                    var methodName = TryParseMethodName(line);
                    if (!string.IsNullOrWhiteSpace(methodName) && !string.IsNullOrWhiteSpace(currentClass))
                    {
                        var fullName = string.IsNullOrWhiteSpace(namespaceName)
                            ? $"{currentClass}.{methodName}"
                            : $"{namespaceName}.{currentClass}.{methodName}";
                        result.Add(new DiscoveredTestMethod(methodName, fullName, file));
                        pendingTestAttribute = false;
                    }
                }
            }
        }

        return result;
    }

    private static string? TryParseClassName(string line)
    {
        var marker = " class ";
        var index = line.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            if (!line.StartsWith("class ", StringComparison.Ordinal))
                return null;
            index = -1;
            marker = "class ";
        }

        var remainder = line[(index + marker.Length)..].Trim();
        return TakeIdentifier(remainder);
    }

    private static string? TryParseMethodName(string line)
    {
        var openParen = line.IndexOf('(');
        if (openParen < 0)
            return null;

        var beforeParen = line[..openParen].Trim();
        if (!beforeParen.Contains("public ", StringComparison.Ordinal) &&
            !beforeParen.Contains("internal ", StringComparison.Ordinal))
        {
            return null;
        }

        var tokens = beforeParen
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length == 0 ? null : TakeIdentifier(tokens[^1]);
    }

    private static string? TakeIdentifier(string value)
    {
        var identifier = new string(value.TakeWhile(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        return string.IsNullOrWhiteSpace(identifier) ? null : identifier;
    }

    private sealed record DiscoveredTestMethod(string Name, string FullyQualifiedName, string SourcePath);
}
