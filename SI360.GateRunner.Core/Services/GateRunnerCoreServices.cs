using Microsoft.Extensions.DependencyInjection;

namespace SI360.GateRunner.Services;

public static class GateRunnerCoreServices
{
    public static IServiceCollection AddGateRunnerCore(this IServiceCollection services)
    {
        services.AddSingleton<ISecretRedactor>(SecretRedactor.Instance);
        services.AddSingleton<IDeploymentMetadataValidator, DeploymentMetadataValidator>();
        services.AddSingleton<ISyntheticProbeRunner, SyntheticProbeRunner>();
        services.AddSingleton<ISupportBundleExporter, SupportBundleExporter>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<DotnetTestRunner>();
        services.AddSingleton<BuildErrorCollector>();
        services.AddSingleton<TrxResultParser>();
        services.AddSingleton<ScorecardAggregator>();
        services.AddSingleton<IDecisionPolicy, DecisionPolicy>();
        services.AddSingleton<IGateDiscoveryService, GateCatalogDriftAnalyzer>();
        services.AddSingleton<GateCatalogDriftAnalyzer>();
        services.AddSingleton<IReportWriter, ReportWriter>();
        services.AddSingleton<ReportWriter>();
        services.AddSingleton<IReportHistoryAnalyzer, ReportHistoryAnalyzer>();
        services.AddSingleton<IGateRunOrchestrator, GateRunOrchestrator>();
        return services;
    }
}
