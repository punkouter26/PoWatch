using Microsoft.Extensions.DependencyInjection;
using PoWatch.Application.Contracts;
using PoWatch.Infrastructure.Persistence;
using PoWatch.Infrastructure.Runtime;

namespace PoWatch.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPoWatchInfrastructure(this IServiceCollection services)
    {
        // Boot-time readiness snapshot: lets the app start and report unhealthy on a dependency failure
        // instead of aborting host construction with an opaque 500.30 (see AzureStorageInitializer).
        services.AddSingleton<StartupReadiness>();

        services.AddSingleton<AzureStorageClients>();

        services.AddSingleton<ISessionRepository, AzureSessionRepository>();
        services.AddSingleton<ISensingLog, AzureSensingLog>();
        services.AddSingleton<IIngestLedger, AzureIngestLedger>();
        services.AddSingleton<IRollupStore, AzureRollupStore>();
        services.AddSingleton<IAchievementStore, AzureAchievementStore>();
        services.AddSingleton<ISnapshotStore, AzureSnapshotStore>();
        services.AddSingleton<IRegularStore, AzureRegularStore>();

        services.AddSingleton<IDiagnosticsProvider, LocalDiagnosticsProvider>();

        // Runs once before the app accepts requests: creates the tables and containers.
        services.AddHostedService<AzureStorageInitializer>();

        return services;
    }
}
