using System.Diagnostics;
using System.IO;
using System.Text;
using SI360.GateRunner.Models;

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
        var exit = await RunDotnetAsync(args, log, ct).ConfigureAwait(false);
        if (exit != 0 || string.IsNullOrWhiteSpace(_settings.UiTestProjectPath))
            return exit;

        return await RunDotnetAsync($"restore \"{_settings.UiTestProjectPath}\" -nologo", log, ct)
            .ConfigureAwait(false);
    }

    public async Task<(int ExitCode, string TrxPath, string StdOut)> RunGateAsync(
        GateDefinition definition,
        string runDir,
        IProgress<string>? log,
        CancellationToken ct)
    {
        Directory.CreateDirectory(runDir);
        var trxName = $"{definition.Id}_{DateTime.Now:yyyyMMdd_HHmmss}.trx";
        var trxPath = Path.Combine(runDir, trxName);

        var sb = new StringBuilder();
        var capture = new Progress<string>(line =>
        {
            sb.AppendLine(line);
            log?.Report(line);
        });

        var projectPath = ResolveTestProjectPath(definition);
        var env = definition.UsesUiTestProject ? BuildUiTestEnvironment() : null;

        var args = new StringBuilder();
        args.Append($"test \"{projectPath}\"");
        if (!definition.UsesUiTestProject)
            args.Append(" --no-build");
        args.Append(" --nologo");
        args.Append($" --filter \"{definition.TestClassFilter}\"");
        args.Append($" --logger \"trx;LogFileName={trxName}\"");
        args.Append($" --results-directory \"{runDir}\"");
        args.Append(" -v normal");

        var exit = await RunDotnetAsync(args.ToString(), capture, ct, env).ConfigureAwait(false);
        return (exit, trxPath, sb.ToString());
    }

    private string ResolveTestProjectPath(GateDefinition definition)
    {
        var path = definition.UsesUiTestProject ? _settings.UiTestProjectPath : _settings.TestProjectPath;
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException($"{definition.DisplayName} test project path is not configured.");
        if (!File.Exists(path))
            throw new FileNotFoundException($"{definition.DisplayName} test project path does not exist: {path}");
        return path;
    }

    private Dictionary<string, string> BuildUiTestEnvironment()
    {
        if (string.IsNullOrWhiteSpace(_settings.UiAppPath))
            throw new InvalidOperationException("UiAppPath is required for FlaUI gates.");
        if (!File.Exists(_settings.UiAppPath))
            throw new FileNotFoundException($"UiAppPath does not exist: {_settings.UiAppPath}");

        var env = new Dictionary<string, string>
        {
            ["SI360_UI_APP_PATH"] = _settings.UiAppPath,
            ["SI360_UI_ARTIFACTS_DIR"] = string.IsNullOrWhiteSpace(_settings.UiArtifactsDirectory)
                ? Path.Combine(_settings.ResultsDirectory, "UIArtifacts")
                : _settings.UiArtifactsDirectory
        };

        if (!string.IsNullOrWhiteSpace(_settings.UiValidPin))
            env["SI360_UI_VALID_PIN"] = _settings.UiValidPin;
        if (!string.IsNullOrWhiteSpace(_settings.UiTestResident))
            env["SI360_UI_TEST_RESIDENT"] = _settings.UiTestResident;

        return env;
    }

    private static async Task<int> RunDotnetAsync(
        string args,
        IProgress<string>? log,
        CancellationToken ct,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var psi = new ProcessStartInfo("dotnet", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (environment is not null)
        {
            foreach (var (key, value) in environment)
                psi.Environment[key] = value;
        }

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
