using Microsoft.Extensions.DependencyInjection;
using SI360.GateRunner.ViewModels;
using SI360.GateRunner.Views;

namespace SI360.GateRunner.Services;

public static class GateRunnerAppServices
{
    public static IServiceCollection AddGateRunnerWpf(this IServiceCollection services)
    {
        services.AddSingleton<ThemeManager>();
        services.AddSingleton<ToastNotifier>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        return services;
    }
}
