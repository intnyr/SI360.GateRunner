using System.Diagnostics;
using System.Text;

namespace SI360.GateRunner.Services;

public sealed record ProcessCommand(
    string FileName,
    string Arguments,
    string WorkingDirectory,
    TimeSpan Timeout,
    string? ArtifactDirectory = null,
    string? ArtifactName = null);

public sealed record ProcessRunResult(
    int ExitCode,
    string StdOut,
    string StdErr,
    bool TimedOut,
    bool Canceled,
    string? ArtifactDirectory);

public interface IProcessRunner
{
    Task<ProcessRunResult> RunAsync(
        ProcessCommand command,
        IProgress<string>? log,
        CancellationToken cancellationToken);
}

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessRunResult> RunAsync(
        ProcessCommand command,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var timedOut = false;
        var canceled = false;

        var psi = new ProcessStartInfo(command.FileName, command.Arguments)
        {
            WorkingDirectory = string.IsNullOrWhiteSpace(command.WorkingDirectory)
                ? Environment.CurrentDirectory
                : command.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stdout.AppendLine(e.Data);
            log?.Report(e.Data);
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stderr.AppendLine(e.Data);
            log?.Report(e.Data);
        };

        using var timeoutCts = new CancellationTokenSource(command.Timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts.Token);

        if (!proc.Start())
            throw new InvalidOperationException($"Failed to start process '{command.FileName}'.");

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        using var reg = linkedCts.Token.Register(() =>
        {
            try
            {
                if (!proc.HasExited)
                    proc.Kill(entireProcessTree: true);
            }
            catch
            {
                // Process may have exited between HasExited and Kill.
            }
        });

        try
        {
            await proc.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            canceled = true;
        }

        var exitCode = proc.HasExited ? proc.ExitCode : -1;
        var result = new ProcessRunResult(
            exitCode,
            stdout.ToString(),
            stderr.ToString(),
            timedOut,
            canceled,
            command.ArtifactDirectory);

        WriteArtifacts(command, result);
        return result;
    }

    private static void WriteArtifacts(ProcessCommand command, ProcessRunResult result)
    {
        if (string.IsNullOrWhiteSpace(command.ArtifactDirectory))
            return;

        Directory.CreateDirectory(command.ArtifactDirectory);
        var name = Sanitize(command.ArtifactName ?? Path.GetFileNameWithoutExtension(command.FileName));
        File.WriteAllText(Path.Combine(command.ArtifactDirectory, $"{name}.command.txt"),
            $"{command.FileName} {command.Arguments}{Environment.NewLine}WorkingDirectory: {command.WorkingDirectory}{Environment.NewLine}TimeoutSeconds: {command.Timeout.TotalSeconds:0}");
        File.WriteAllText(Path.Combine(command.ArtifactDirectory, $"{name}.stdout.log"), result.StdOut);
        File.WriteAllText(Path.Combine(command.ArtifactDirectory, $"{name}.stderr.log"), result.StdErr);
        File.WriteAllText(Path.Combine(command.ArtifactDirectory, $"{name}.exit.txt"),
            $"ExitCode: {result.ExitCode}{Environment.NewLine}TimedOut: {result.TimedOut}{Environment.NewLine}Canceled: {result.Canceled}");
    }

    private static string Sanitize(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            value = value.Replace(c, '_');
        return string.IsNullOrWhiteSpace(value) ? "process" : value;
    }
}
