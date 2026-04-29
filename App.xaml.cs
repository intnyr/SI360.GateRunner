using System.Diagnostics;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SI360.GateRunner.Services;
using SI360.GateRunner.ViewModels;
using SI360.GateRunner.Views;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace SI360.GateRunner;

public partial class App : Application
{
    public IServiceProvider Services { get; private set; } = null!;

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

        var settings = RunnerSettings.LoadOrDiscover();

        var sc = new ServiceCollection();
        sc.AddSingleton(settings);
        sc.AddSingleton<DotnetTestRunner>();
        sc.AddSingleton<BuildErrorCollector>();
        sc.AddSingleton<TrxResultParser>();
        sc.AddSingleton<ScorecardAggregator>();
        sc.AddSingleton<ReportWriter>();
        sc.AddSingleton<ThemeManager>();
        sc.AddSingleton<ToastNotifier>();
        sc.AddSingleton<MainViewModel>();
        sc.AddSingleton<MainWindow>();
        Services = sc.BuildServiceProvider();

        Exit += (_, _) => Services.GetService<ToastNotifier>()?.Dispose();

        var vm = Services.GetRequiredService<MainViewModel>();
        vm.RefreshSettings();
        var main = Services.GetRequiredService<MainWindow>();
        main.DataContext = vm;
        MainWindow = main;
        main.Show();
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
}
