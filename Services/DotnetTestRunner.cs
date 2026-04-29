using System.Diagnostics;
using System.IO;
using System.Text;

namespace SI360.GateRunner.Services;

public sealed class DotnetTestRunner
{
    private readonly RunnerSettings _settings;

    public DotnetTestRunner(RunnerSettings settings) => _settings = settings;

    public async Task<int> RestoreAsync(IProgress<string>? log, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_settings.TestProjectPath))
            throw new InvalidOperationException("TestProjectPath not configured.");
        var args = $"restore \"{_settings.TestProjectPath}\" -nologo";
        return await RunDotnetAsync(args, log, ct).ConfigureAwait(false);
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

        var exit = await RunDotnetAsync(args.ToString(), capture, ct).ConfigureAwait(false);
        return (exit, trxPath, sb.ToString());
    }

    private static async Task<int> RunDotnetAsync(string args, IProgress<string>? log, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("dotnet", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) log?.Report(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) log?.Report(e.Data); };

        if (!proc.Start()) throw new InvalidOperationException("Failed to start dotnet process.");
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        using var reg = ct.Register(() =>
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); }
            catch { }
        });

        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        return proc.ExitCode;
    }
}
