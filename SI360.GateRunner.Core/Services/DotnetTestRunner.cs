using System.Diagnostics;
using System.IO;
using System.Text;

namespace SI360.GateRunner.Services;

public sealed class DotnetTestRunner
{
    private readonly RunnerSettings _settings;
    private readonly IProcessRunner _processRunner;

    public DotnetTestRunner(RunnerSettings settings, IProcessRunner processRunner)
    {
        _settings = settings;
        _processRunner = processRunner;
    }

    public async Task<ProcessRunResult> RestoreAsync(IProgress<string>? log, CancellationToken ct, string? artifactDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(_settings.TestProjectPath))
            throw new InvalidOperationException("TestProjectPath not configured.");
        var args = $"restore \"{_settings.TestProjectPath}\" -nologo";
        return await _processRunner.RunAsync(
            new ProcessCommand(
                "dotnet",
                args,
                Path.GetDirectoryName(_settings.TestProjectPath) ?? Environment.CurrentDirectory,
                TimeSpan.FromSeconds(Math.Max(1, _settings.RestoreTimeoutSeconds)),
                artifactDirectory,
                "restore"),
            log,
            ct).ConfigureAwait(false);
    }

    public async Task<(int ExitCode, string TrxPath, string StdOut)> RunGateAsync(
        string gateId,
        string filter,
        string runDir,
        IProgress<string>? log,
        CancellationToken ct)
    {
        Directory.CreateDirectory(runDir);
        var trxName = $"{gateId}_{DateTime.Now:yyyyMMdd_HHmmss}.trx";
        var trxPath = Path.Combine(runDir, trxName);

        var sb = new StringBuilder();
        var capture = new Progress<string>(line =>
        {
            sb.AppendLine(line);
            log?.Report(line);
        });

        var args = new StringBuilder();
        args.Append($"test \"{_settings.TestProjectPath}\"");
        args.Append(" --no-build --nologo");
        args.Append($" --filter \"{filter}\"");
        args.Append($" --logger \"trx;LogFileName={trxName}\"");
        args.Append($" --results-directory \"{runDir}\"");
        args.Append(" -v normal");

        var result = await _processRunner.RunAsync(
            new ProcessCommand(
                "dotnet",
                args.ToString(),
                Path.GetDirectoryName(_settings.TestProjectPath) ?? Environment.CurrentDirectory,
                TimeSpan.FromSeconds(Math.Max(1, _settings.GateTimeoutSeconds)),
                runDir,
                $"gate-{gateId}"),
            capture,
            ct).ConfigureAwait(false);

        var output = sb.ToString();
        if (result.TimedOut)
            output += $"{Environment.NewLine}[TIMEOUT] Gate exceeded {_settings.GateTimeoutSeconds}s.";
        if (result.Canceled)
            output += $"{Environment.NewLine}[CANCELED] Gate run was canceled.";

        return (result.ExitCode, trxPath, output);
    }
}
