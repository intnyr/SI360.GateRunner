using System.Text.RegularExpressions;
using SI360.GateRunner.Models;

namespace SI360.GateRunner.Services;

public sealed partial class BuildErrorCollector
{
    private static readonly Regex IssueRegex = MakeIssueRegex();

    [GeneratedRegex(@"^(?<file>.+?)\((?<line>\d+)(?:,(?<col>\d+))?\):\s*(?<severity>error|warning)\s+(?<code>[A-Z]+\d+):\s*(?<msg>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex MakeIssueRegex();

    private readonly RunnerSettings _settings;
    private readonly IProcessRunner _processRunner;

    public BuildErrorCollector(RunnerSettings settings, IProcessRunner processRunner)
    {
        _settings = settings;
        _processRunner = processRunner;
    }

    public async Task<(int ExitCode, List<QualityIssue> Issues)> BuildAsync(
        IProgress<string>? log,
        CancellationToken ct,
        string? artifactDirectory = null)
    {
        var issues = new List<QualityIssue>();

        void Handle(string? data)
        {
            if (string.IsNullOrEmpty(data)) return;
            log?.Report(data);
            var m = IssueRegex.Match(data);
            if (m.Success)
            {
                var severity = string.Equals(m.Groups["severity"].Value, "error", StringComparison.OrdinalIgnoreCase)
                    ? QualityIssueSeverity.Error
                    : QualityIssueSeverity.Warning;
                var file = m.Groups["file"].Value.Trim();
                var line = m.Groups["line"].Value;
                var col = m.Groups["col"].Success ? m.Groups["col"].Value : "0";
                var code = m.Groups["code"].Value;
                issues.Add(new QualityIssue(
                    $"build:{severity}:{code}:{file}:{line}:{col}",
                    severity,
                    QualityIssueSource.Build,
                    $"{file}:{line}",
                    m.Groups["code"].Value,
                    m.Groups["msg"].Value.Trim(),
                    severity == QualityIssueSeverity.Warning ? 2.0 : 100.0,
                    severity == QualityIssueSeverity.Warning
                        ? "HOLD: build warning applies a strict quality penalty."
                        : "NO-GO: build error fails the quality gate.",
                    artifactDirectory is null ? null : Path.Combine(artifactDirectory, "build.stdout.log")));
            }
        }

        var result = await _processRunner.RunAsync(
            GateRunnerCommands.Build(_settings, artifactDirectory),
            new InlineProgress(Handle),
            ct).ConfigureAwait(false);

        if (result.TimedOut)
            log?.Report($"[TIMEOUT] Build exceeded {_settings.BuildTimeoutSeconds}s.");
        if (result.Canceled)
            log?.Report("[CANCELED] Build was canceled.");

        return (result.ExitCode, issues);
    }

    private sealed class InlineProgress : IProgress<string>
    {
        private readonly Action<string?> _handler;

        public InlineProgress(Action<string?> handler)
        {
            _handler = handler;
        }

        public void Report(string value) => _handler(value);
    }
}
