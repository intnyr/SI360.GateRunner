using System.Diagnostics;
using System.Text.RegularExpressions;
using SI360.GateRunner.Models;

namespace SI360.GateRunner.Services;

public sealed partial class BuildErrorCollector
{
    private static readonly Regex ErrorRegex = MakeErrorRegex();

    [GeneratedRegex(@"^(?<file>.+?)\((?<line>\d+),(?<col>\d+)\):\s*error\s+(?<code>[A-Z]+\d+):\s*(?<msg>.+)$", RegexOptions.Compiled)]
    private static partial Regex MakeErrorRegex();

    private readonly RunnerSettings _settings;

    public BuildErrorCollector(RunnerSettings settings) => _settings = settings;

    public async Task<(int ExitCode, List<BuildError> Errors)> BuildAsync(IProgress<string>? log, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_settings.SolutionPath))
            throw new InvalidOperationException("SolutionPath not configured.");

        var errors = new List<BuildError>();
        var args = $"build \"{_settings.SolutionPath}\" -c Release -p:GenerateFullPaths=true -nologo -clp:ErrorsOnly";

        var psi = new ProcessStartInfo("dotnet", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

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

        proc.OutputDataReceived += (_, e) => Handle(e.Data);
        proc.ErrorDataReceived += (_, e) => Handle(e.Data);

        if (!proc.Start()) throw new InvalidOperationException("Failed to start dotnet.");
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        using var reg = ct.Register(() => { try { if (!proc.HasExited) proc.Kill(true); } catch { } });
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        return (proc.ExitCode, errors);
    }
}
