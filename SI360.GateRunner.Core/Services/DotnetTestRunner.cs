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
        return await _processRunner.RunAsync(
            GateRunnerCommands.Restore(_settings, artifactDirectory),
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
        var trxName = $"{gateId}_{DateTime.UtcNow:yyyyMMdd_HHmmss}Z.trx";
        var trxPath = Path.Combine(runDir, trxName);

        var sb = new StringBuilder();
        var capture = new Progress<string>(line =>
        {
            sb.AppendLine(line);
            log?.Report(line);
        });

        var result = await _processRunner.RunAsync(
            GateRunnerCommands.Gate(_settings, gateId, filter, runDir, trxName),
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
