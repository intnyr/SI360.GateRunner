using System.Diagnostics;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SI360.GateRunner.Services;
using SI360.GateRunner.ViewModels;
using SI360.GateRunner.Views;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace SI360.GateRunner;

public partial class App : Application
{
    public IServiceProvider Services { get; private set; } = null!;
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!ProbeDotnetCli())
        {
            MessageBox.Show(
                "dotnet CLI not found. Install .NET 8 SDK: https://dotnet.microsoft.com/download",
                "SI360 Gate Runner",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        _host = Host.CreateDefaultBuilder(e.Args)
            .ConfigureAppConfiguration(builder =>
            {
                builder.SetBasePath(AppContext.BaseDirectory);
                builder.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                builder.AddEnvironmentVariables(prefix: "GATERUNNER_");
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                logging.AddDebug();
            })
            .ConfigureServices((context, services) =>
            {
                var settings = RunnerSettings.LoadOrDiscover();
                context.Configuration.GetSection("RunnerSettings").Bind(settings);
                settings.ApplyEnvironmentVariables();
                ValidateSettings(settings);
                services.AddSingleton(settings);
                services.AddGateRunnerCore();
                services.AddGateRunnerWpf();
            })
            .Build();

        Services = _host.Services;

        var vm = Services.GetRequiredService<MainViewModel>();
        vm.RefreshSettings();
        var main = Services.GetRequiredService<MainWindow>();
        main.DataContext = vm;
        MainWindow = main;
        main.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Services.GetService<ToastNotifier>()?.Dispose();
        _host?.Dispose();
        base.OnExit(e);
    }

    private static bool ProbeDotnetCli()
    {
        try
        {
            var psi = new ProcessStartInfo("dotnet", "--version")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p is null) return false;
            p.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    private static void ValidateSettings(RunnerSettings settings)
    {
        if (settings.GateTimeoutSeconds <= 0)
            settings.GateTimeoutSeconds = 900;
        if (settings.BuildTimeoutSeconds <= 0)
            settings.BuildTimeoutSeconds = 600;
        if (settings.RestoreTimeoutSeconds <= 0)
            settings.RestoreTimeoutSeconds = 300;
        if (settings.ProbeTimeoutSeconds <= 0)
            settings.ProbeTimeoutSeconds = 30;
        if (settings.ReportRetentionDays <= 0)
            settings.ReportRetentionDays = 30;
        if (string.IsNullOrWhiteSpace(settings.BuildConfiguration))
            settings.BuildConfiguration = "Release";
        if (string.IsNullOrWhiteSpace(settings.ProbeMode))
            settings.ProbeMode = "ReadOnly";
    }
}
