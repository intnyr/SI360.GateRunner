using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SI360.GateRunner.Models;

namespace SI360.GateRunner.Services;

public sealed partial class TrxResultParser
{
    private static readonly XNamespace Ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    [GeneratedRegex(@"in\s+(?<file>[^:]+):line\s+(?<line>\d+)", RegexOptions.Compiled)]
    private static partial Regex StackLocationRegex();

    public IReadOnlyList<TestOutcome> Parse(string trxPath, string gateName)
    {
        if (!File.Exists(trxPath)) return Array.Empty<TestOutcome>();

        var doc = XDocument.Load(trxPath);
        var results = doc.Descendants(Ns + "UnitTestResult");
        var list = new List<TestOutcome>();

        foreach (var r in results)
        {
            var name = (string?)r.Attribute("testName") ?? "<unknown>";
            var outcomeStr = (string?)r.Attribute("outcome") ?? "Unknown";
            var durationStr = (string?)r.Attribute("duration");
            var duration = TimeSpan.TryParse(durationStr, out var d) ? d : TimeSpan.Zero;

            var output = r.Element(Ns + "Output");
            var stdOut = output?.Element(Ns + "StdOut")?.Value;
            var errInfo = output?.Element(Ns + "ErrorInfo");
            var errMsg = errInfo?.Element(Ns + "Message")?.Value?.Trim();
            var errStack = errInfo?.Element(Ns + "StackTrace")?.Value;

            string? filePath = null;
            int? lineNumber = null;
            if (!string.IsNullOrEmpty(errStack))
            {
                var m = StackLocationRegex().Match(errStack);
                if (m.Success)
                {
                    filePath = m.Groups["file"].Value.Trim();
                    if (int.TryParse(m.Groups["line"].Value, out var ln)) lineNumber = ln;
                }
            }

            list.Add(new TestOutcome(
                gateName,
                name,
                MapStatus(outcomeStr),
                duration,
                FirstLine(errMsg),
                Truncate(errStack, 40),
                stdOut,
                filePath,
                lineNumber));
        }
        return list;
    }

    private static TestStatus MapStatus(string s) => s switch
    {
        "Passed" => TestStatus.Passed,
        "Failed" => TestStatus.Failed,
        "NotExecuted" or "Skipped" => TestStatus.Skipped,
        _ => TestStatus.Unknown
    };

    private static string? FirstLine(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        using var reader = new StringReader(s);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (!string.IsNullOrWhiteSpace(line)) return line.Trim();
        }
        return null;
    }

    private static string? Truncate(string? s, int maxLines)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var lines = s.Split('\n');
        if (lines.Length <= maxLines) return s;
        return string.Join('\n', lines.Take(maxLines)) + $"\n… ({lines.Length - maxLines} more lines)";
    }
}
