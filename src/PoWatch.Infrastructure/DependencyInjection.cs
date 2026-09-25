using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PoWatch.Application.Contracts;
using PoWatch.Application.Options;
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

        // Stat-cam stores: Azure when storage is configured, in-memory otherwise.
        AddStore<ISessionRepository, AzureSessionRepository, InMemorySessionRepository>(services);
        AddStore<ISensingLog, AzureSensingLog, InMemorySensingLog>(services);
        AddStore<IIngestLedger, AzureIngestLedger, InMemoryIngestLedger>(services);
        AddStore<IRollupStore, AzureRollupStore, InMemoryRollupStore>(services);
        AddStore<IAchievementStore, AzureAchievementStore, InMemoryAchievementStore>(services);
        AddStore<ISnapshotStore, AzureSnapshotStore, InMemorySnapshotStore>(services);
        AddStore<IRegularStore, AzureRegularStore, InMemoryRegularStore>(services);

        services.AddSingleton<IDiagnosticsProvider, LocalDiagnosticsProvider>();

        // Runs once before the app accepts requests: creates the tables and containers.
        services.AddHostedService<AzureStorageInitializer>();

        return services;
    }

    private static void AddStore<TContract, TAzure, TInMemory>(IServiceCollection services)
        where TContract : class
        where TAzure : class, TContract
        where TInMemory : class, TContract
    {
        services.AddSingleton<TAzure>();
        services.AddSingleton<TInMemory>();
        services.AddSingleton<TContract>(sp =>
            UseAzureStorage(sp.GetRequiredService<IOptions<AzureStorageOptions>>().Value)
                ? sp.GetRequiredService<TAzure>()
                : sp.GetRequiredService<TInMemory>());
    }

    private static bool UseAzureStorage(AzureStorageOptions options) =>
        !string.IsNullOrWhiteSpace(options.ConnectionString)
        || !string.IsNullOrWhiteSpace(options.ServiceUri);
}
