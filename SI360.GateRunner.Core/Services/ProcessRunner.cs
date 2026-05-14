using System.Diagnostics;
using System.Text;

namespace SI360.GateRunner.Services;

public sealed record ProcessCommand(
    string FileName,
    string Arguments,
    string WorkingDirectory,
    TimeSpan Timeout,
    string? ArtifactDirectory = null,
    string? ArtifactName = null,
    IReadOnlyDictionary<string, string>? EnvironmentVariables = null);

public sealed record ProcessRunResult(
    int ExitCode,
    string StdOut,
    string StdErr,
    bool TimedOut,
    bool Canceled,
    string? ArtifactDirectory,
    string Diagnostics = "");

public interface IProcessRunner
{
    Task<ProcessRunResult> RunAsync(
        ProcessCommand command,
        IProgress<string>? log,
        CancellationToken cancellationToken);
}

public sealed class ProcessRunner : IProcessRunner
{
    private readonly ISecretRedactor _redactor;

    public ProcessRunner()
        : this(SecretRedactor.Instance)
    {
    }

    public ProcessRunner(ISecretRedactor redactor)
    {
        _redactor = redactor;
    }

    public async Task<ProcessRunResult> RunAsync(
        ProcessCommand command,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var stdoutLock = new object();
        var stderrLock = new object();
        var diagnostics = new StringBuilder();
        var diagnosticsLock = new object();
        var timedOut = false;
        var canceled = false;
        var killRequested = false;

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

        if (command.EnvironmentVariables is not null)
        {
            foreach (var pair in command.EnvironmentVariables)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key))
                    psi.Environment[pair.Key] = pair.Value;
            }
        }

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            var line = _redactor.Redact(e.Data);
            lock (stdoutLock)
                stdout.AppendLine(line);
            log?.Report(line);
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            var line = _redactor.Redact(e.Data);
            lock (stderrLock)
                stderr.AppendLine(line);
            log?.Report(line);
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
                var reason = timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested
                    ? "timeout"
                    : "cancellation";
                killRequested = true;
                AppendProcessDiagnostics(diagnostics, diagnosticsLock, $"Process tree kill requested after {reason}.", proc);
                if (!proc.HasExited)
                    proc.Kill(entireProcessTree: true);
                AppendProcessDiagnostics(diagnostics, diagnosticsLock, "Process tree kill command completed.", proc);
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
            AppendProcessDiagnostics(diagnostics, diagnosticsLock, "Process wait ended because the timeout elapsed.", proc);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            canceled = true;
            AppendProcessDiagnostics(diagnostics, diagnosticsLock, "Process wait ended because cancellation was requested.", proc);
        }

        var exitCode = proc.HasExited ? proc.ExitCode : -1;
        if (timedOut && proc.HasExited && !killRequested)
            timedOut = false;
        if (proc.HasExited)
            proc.WaitForExit();

        string stdoutText;
        string stderrText;
        lock (stdoutLock)
            stdoutText = stdout.ToString();
        lock (stderrLock)
            stderrText = stderr.ToString();

        var result = new ProcessRunResult(
            exitCode,
            stdoutText,
            stderrText,
            timedOut,
            canceled,
            command.ArtifactDirectory,
            diagnostics.ToString());

        WriteArtifacts(command, result, _redactor);
        return result;
    }

    private static void WriteArtifacts(ProcessCommand command, ProcessRunResult result, ISecretRedactor redactor)
    {
        if (string.IsNullOrWhiteSpace(command.ArtifactDirectory))
            return;

        Directory.CreateDirectory(command.ArtifactDirectory);
        var name = Sanitize(command.ArtifactName ?? Path.GetFileNameWithoutExtension(command.FileName));
        File.WriteAllText(Path.Combine(command.ArtifactDirectory, $"{name}.command.txt"),
            redactor.Redact($"{command.FileName} {command.Arguments}{Environment.NewLine}WorkingDirectory: {command.WorkingDirectory}{Environment.NewLine}TimeoutSeconds: {command.Timeout.TotalSeconds:0}{FormatEnvironment(command.EnvironmentVariables)}"));
        File.WriteAllText(Path.Combine(command.ArtifactDirectory, $"{name}.stdout.log"), redactor.Redact(result.StdOut));
        File.WriteAllText(Path.Combine(command.ArtifactDirectory, $"{name}.stderr.log"), redactor.Redact(result.StdErr));
        File.WriteAllText(Path.Combine(command.ArtifactDirectory, $"{name}.exit.txt"),
            $"ExitCode: {result.ExitCode}{Environment.NewLine}TimedOut: {result.TimedOut}{Environment.NewLine}Canceled: {result.Canceled}");
        if (!string.IsNullOrWhiteSpace(result.Diagnostics) || result.TimedOut || result.Canceled)
        {
            File.WriteAllText(
                Path.Combine(command.ArtifactDirectory, $"{name}.diagnostics.txt"),
                redactor.Redact(string.IsNullOrWhiteSpace(result.Diagnostics)
                    ? "No process diagnostics were captured."
                    : result.Diagnostics));
        }
    }

    private static void AppendProcessDiagnostics(
        StringBuilder diagnostics,
        object diagnosticsLock,
        string heading,
        Process process)
    {
        lock (diagnosticsLock)
        {
            diagnostics.AppendLine("==== Process snapshot ====");
            diagnostics.AppendLine($"UTC: {DateTime.UtcNow:O}");
            diagnostics.AppendLine(heading);
            AppendProcessLine(diagnostics, "Root", process);
            diagnostics.AppendLine("Known FlaUI-related processes:");

            Process[] processes;
            try
            {
                processes = Process.GetProcesses();
            }
            catch (Exception ex)
            {
                diagnostics.AppendLine($"Unable to enumerate processes: {ex.GetType().Name}: {ex.Message}");
                diagnostics.AppendLine();
                return;
            }

            foreach (var candidate in processes
                         .Where(IsDiagnosticsCandidate)
                         .OrderBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(p => p.Id))
            {
                using (candidate)
                    AppendProcessLine(diagnostics, "Candidate", candidate);
            }

            diagnostics.AppendLine();
        }
    }

    private static bool IsDiagnosticsCandidate(Process process)
    {
        try
        {
            var name = process.ProcessName;
            return name.Equals("dotnet", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("testhost", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("testhost.x86", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("vstest.console", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("SI360.UI", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("SI360.GateRunner", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("SI360.GateRunner.Cli", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void AppendProcessLine(StringBuilder diagnostics, string prefix, Process process)
    {
        try
        {
            diagnostics.Append($"{prefix}: pid={process.Id}; name={process.ProcessName}; exited={SafeHasExited(process)}");
            var title = SafeRead(() => process.MainWindowTitle);
            if (!string.IsNullOrWhiteSpace(title))
                diagnostics.Append($"; window=\"{title}\"");
            var startTime = SafeRead(() => process.StartTime.ToString("O"));
            if (!string.IsNullOrWhiteSpace(startTime))
                diagnostics.Append($"; start={startTime}");
            var path = SafeRead(() => process.MainModule?.FileName ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(path))
                diagnostics.Append($"; path=\"{path}\"");
            diagnostics.AppendLine();
        }
        catch (Exception ex)
        {
            diagnostics.AppendLine($"{prefix}: unable to read process details: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool SafeHasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static string SafeRead(Func<string> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string FormatEnvironment(IReadOnlyDictionary<string, string>? environmentVariables)
    {
        if (environmentVariables is null || environmentVariables.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("Environment:");
        foreach (var pair in environmentVariables.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            sb.AppendLine($"{pair.Key}={pair.Value}");
        return sb.ToString();
    }

    private static string Sanitize(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            value = value.Replace(c, '_');
        return string.IsNullOrWhiteSpace(value) ? "process" : value;
    }
}
