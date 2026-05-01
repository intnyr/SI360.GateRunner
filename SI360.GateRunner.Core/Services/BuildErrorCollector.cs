using System.Text.RegularExpressions;
using SI360.GateRunner.Models;

namespace SI360.GateRunner.Services;

public sealed partial class BuildErrorCollector
{
    private static readonly Regex ErrorRegex = MakeErrorRegex();

    [GeneratedRegex(@"^(?<file>.+?)\((?<line>\d+),(?<col>\d+)\):\s*error\s+(?<code>[A-Z]+\d+):\s*(?<msg>.+)$", RegexOptions.Compiled)]
    private static partial Regex MakeErrorRegex();

    private readonly RunnerSettings _settings;
    private readonly IProcessRunner _processRunner;

    public BuildErrorCollector(RunnerSettings settings, IProcessRunner processRunner)
    {
        _settings = settings;
        _processRunner = processRunner;
    }

    public async Task<(int ExitCode, List<BuildError> Errors)> BuildAsync(
        IProgress<string>? log,
        CancellationToken ct,
        string? artifactDirectory = null)
    {
        var errors = new List<BuildError>();

        void Handle(string? data)
        {
            if (string.IsNullOrEmpty(data)) return;
            log?.Report(data);
            var m = ErrorRegex.Match(data);
            if (m.Success)
            {
                errors.Add(new BuildError(
                    m.Groups["file"].Value.Trim(),
                    int.Parse(m.Groups["line"].Value),
                    int.Parse(m.Groups["col"].Value),
                    m.Groups["code"].Value,
                    m.Groups["msg"].Value.Trim()));
            }
        }

        var capture = new Progress<string>(Handle);
        var result = await _processRunner.RunAsync(
            GateRunnerCommands.Build(_settings, artifactDirectory),
            capture,
            ct).ConfigureAwait(false);

        if (result.TimedOut)
            log?.Report($"[TIMEOUT] Build exceeded {_settings.BuildTimeoutSeconds}s.");
        if (result.Canceled)
            log?.Report("[CANCELED] Build was canceled.");

        return (result.ExitCode, errors);
    }
}
