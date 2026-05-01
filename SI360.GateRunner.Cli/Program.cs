using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SI360.GateRunner.Models;
using SI360.GateRunner.Services;

var command = args.Length == 0 ? "help" : args[0].ToLowerInvariant();
var options = ParseOptions(args.Skip(1).ToArray());

using var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration(builder =>
    {
        builder.SetBasePath(AppContext.BaseDirectory);
        builder.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
        builder.AddEnvironmentVariables(prefix: "GATERUNNER_");
    })
    .ConfigureServices((context, services) =>
    {
        var settings = RunnerSettings.LoadOrDiscover();
        context.Configuration.GetSection("RunnerSettings").Bind(settings);
        settings.ApplyEnvironmentVariables();
        ApplyOptions(settings, options);
        services.AddSingleton(settings);
        services.AddGateRunnerCore();
    })
    .Build();

try
{
    return command switch
    {
        "discover" => Discover(host.Services),
        "validate-catalog" => ValidateCatalog(host.Services),
        "validate-metadata" => ValidateMetadata(host.Services),
        "run-probes" => await RunProbesAsync(host.Services),
        "run" => await RunAsync(host.Services),
        "summarize" => Summarize(host.Services, options),
        _ => Help()
    };
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("GateRunner CLI canceled.");
    return 4;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 5;
}

static int Discover(IServiceProvider services)
{
    var settings = services.GetRequiredService<RunnerSettings>();
    EnsureValid(settings);
    var discovery = services.GetRequiredService<IGateDiscoveryService>();
    foreach (var gate in discovery.Discover(settings))
        Console.WriteLine($"{gate.Id}\t{gate.TestCount}\t{gate.FilePath}");
    return 0;
}

static int ValidateCatalog(IServiceProvider services)
{
    var settings = services.GetRequiredService<RunnerSettings>();
    EnsureValid(settings);
    var discovery = services.GetRequiredService<IGateDiscoveryService>();
    var warnings = discovery.Validate(settings);
    foreach (var warning in warnings)
        Console.WriteLine($"{warning.Code}: {warning.Message}");
    return warnings.Count == 0 ? 0 : 3;
}

static int ValidateMetadata(IServiceProvider services)
{
    var settings = services.GetRequiredService<RunnerSettings>();
    var validator = services.GetRequiredService<IDeploymentMetadataValidator>();
    var result = validator.LoadAndValidate(settings.DeploymentMetadataPath);
    foreach (var issue in result.Issues)
        Console.WriteLine($"{issue.Severity} {issue.Code} {issue.Field}: {issue.Message}");

    if (result.IsValid)
        Console.WriteLine("Deployment metadata is valid.");

    return result.IsValid ? 0 : 3;
}

static async Task<int> RunProbesAsync(IServiceProvider services)
{
    var settings = services.GetRequiredService<RunnerSettings>();
    var metadataValidator = services.GetRequiredService<IDeploymentMetadataValidator>();
    var probeRunner = services.GetRequiredService<ISyntheticProbeRunner>();
    var metadataResult = metadataValidator.LoadAndValidate(settings.DeploymentMetadataPath);
    var results = await probeRunner.RunAsync(settings, metadataResult, CancellationToken.None);
    foreach (var result in results)
        Console.WriteLine($"{result.Status}\t{result.Id}\t{result.Endpoint}\t{result.Diagnostics}");

    return results.Any(r => r.Status is SyntheticProbeStatus.Failed or SyntheticProbeStatus.Error) ? 3 : 0;
}

static async Task<int> RunAsync(IServiceProvider services)
{
    EnsureValid(services.GetRequiredService<RunnerSettings>());
    var orchestrator = services.GetRequiredService<IGateRunOrchestrator>();
    var progress = new Progress<string>(Console.WriteLine);
    var summary = await orchestrator.RunAsync(new GateRunRequest(), progress, CancellationToken.None);
    Console.WriteLine($"Decision: {Label(summary.Decision)}");
    Console.WriteLine($"Markdown: {summary.ReportMarkdownPath}");
    Console.WriteLine($"JSON: {summary.ReportJsonPath}");

    return summary.Decision switch
    {
        DeployDecision.Go => 0,
        DeployDecision.Hold => 1,
        _ => 2
    };
}

static int Summarize(IServiceProvider services, IReadOnlyDictionary<string, string> options)
{
    var settings = services.GetRequiredService<RunnerSettings>();
    var path = options.TryGetValue("report", out var explicitPath)
        ? explicitPath
        : Directory.EnumerateFiles(settings.ResultsDirectory, "GateRun_*.json")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault() ?? string.Empty;

    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
    {
        Console.Error.WriteLine("No report found. Pass --report <path> or run gates first.");
        return 5;
    }

    Console.WriteLine(File.ReadAllText(path));
    return 0;
}

static int Help()
{
    Console.WriteLine("""
    SI360.GateRunner.Cli

    Commands:
      discover                         List discovered pre-deployment gates.
      validate-catalog                 Validate GateRunner catalog against SI360.Tests source.
      validate-metadata                Validate deployment metadata JSON.
      run-probes                       Run read-only synthetic runtime probes.
      run                              Run restore, build, all gates, and emit reports.
      summarize [--report <path>]      Print a JSON report.

    Options:
      --solution <path>                Override SI360 solution path.
      --test-project <path>            Override SI360.Tests project path.
      --results <path>                 Override results directory.
      --configuration <name>           Override dotnet build configuration.
      --metadata <path>                Deployment metadata JSON path.
      --probe-mode <mode>              Disabled, ReadOnly, or Active.
      --probe-timeout-seconds <number> Override synthetic probe timeout.
      --retention-days <number>        Override report retention window.
      --support-bundle <path>          Support bundle output path.
      --gate-timeout-seconds <number>  Override per-gate timeout.
    """);
    return 0;
}

static Dictionary<string, string> ParseOptions(string[] args)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal))
            continue;
        var key = args[i][2..];
        var value = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
            ? args[++i]
            : "true";
        result[key] = value;
    }
    return result;
}

static void ApplyOptions(RunnerSettings settings, IReadOnlyDictionary<string, string> options)
{
    if (options.TryGetValue("solution", out var solution))
        settings.SolutionPath = solution;
    if (options.TryGetValue("test-project", out var testProject))
        settings.TestProjectPath = testProject;
    if (options.TryGetValue("results", out var results))
        settings.ResultsDirectory = results;
    if (options.TryGetValue("configuration", out var configuration) ||
        options.TryGetValue("build-configuration", out configuration))
        settings.BuildConfiguration = configuration;
    if (options.TryGetValue("metadata", out var metadata) ||
        options.TryGetValue("deployment-metadata", out metadata))
        settings.DeploymentMetadataPath = metadata;
    if (options.TryGetValue("probe-mode", out var probeMode))
        settings.ProbeMode = probeMode;
    if (options.TryGetValue("probe-timeout-seconds", out var probeTimeout) &&
        int.TryParse(probeTimeout, out var probeTimeoutSeconds))
        settings.ProbeTimeoutSeconds = probeTimeoutSeconds;
    if (options.TryGetValue("retention-days", out var retentionDays) &&
        int.TryParse(retentionDays, out var retentionDaysValue))
        settings.ReportRetentionDays = retentionDaysValue;
    if (options.TryGetValue("support-bundle", out var supportBundle))
        settings.SupportBundleOutputPath = supportBundle;
    if (options.TryGetValue("gate-timeout-seconds", out var gateTimeout) &&
        int.TryParse(gateTimeout, out var gateTimeoutSeconds))
        settings.GateTimeoutSeconds = gateTimeoutSeconds;
}

static void EnsureValid(RunnerSettings settings)
{
    var errors = settings.Validate();
    if (errors.Count > 0)
        throw new InvalidOperationException("Invalid GateRunner settings:" + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(e => $"- {e}")));
}

static string Label(DeployDecision decision) => decision switch
{
    DeployDecision.Go => "GO",
    DeployDecision.Hold => "HOLD",
    _ => "NO-GO"
};
