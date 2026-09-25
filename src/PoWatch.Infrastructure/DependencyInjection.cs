using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PoWatch.Application.Contracts;
using PoWatch.Application.Options;
using PoWatch.Application.Services;
using PoWatch.Infrastructure.Persistence;
using PoWatch.Infrastructure.Runtime;

namespace PoWatch.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPoWatchInfrastructure(this IServiceCollection services)
    {
        // Handoff Coach summarizer — template is always registered; Azure OpenAI used when configured
        services.AddScoped<TemplateHandoffSummarizer>();

        // Typed HttpClient for Azure OpenAI backed by a native .NET resilience pipeline
        // (retry + circuit breaker + timeout + rate limiter) via AddStandardResilienceHandler.
        services.AddHttpClient<AzureOpenAiHandoffSummarizer>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(45);
            })
            .AddStandardResilienceHandler();

        // Multi-provider summarizer (Azure OpenAI + local Ollama edge gateway)
        services.AddHttpClient<MultiProviderHandoffSummarizer>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(60);
            })
            .AddStandardResilienceHandler();

        services.AddScoped<IHandoffSummarizer>(sp =>
            sp.GetRequiredService<MultiProviderHandoffSummarizer>());

        // Boot-time readiness snapshot: lets the app start and report unhealthy on a dependency failure
        // instead of aborting host construction with an opaque 500.30 (see AzureStorageInitializer).
        services.AddSingleton<StartupReadiness>();

        services.AddSingleton<AzureStorageClients>();

        services.AddSingleton<InMemoryObservationRepository>();
        services.AddSingleton<InMemorySubjectRepository>();
        services.AddSingleton<AzureObservationRepository>();
        services.AddSingleton<AzureSubjectRepository>();
        services.AddSingleton<IBlobSasProvider, AzureBlobSasProvider>();
        services.AddSingleton<AzureStorageResetService>();
        services.AddSingleton<IStorageResetService>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AzureStorageOptions>>().Value;
            return UseAzureStorage(options)
                ? sp.GetRequiredService<AzureStorageResetService>()
                : throw new InvalidOperationException("IStorageResetService is only supported with Azure Table Storage.");
        });

        services.AddSingleton<IObservationRepository>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AzureStorageOptions>>().Value;
            return UseAzureStorage(options)
                ? sp.GetRequiredService<AzureObservationRepository>()
                : sp.GetRequiredService<InMemoryObservationRepository>();
        });

        services.AddSingleton<ISubjectRepository>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AzureStorageOptions>>().Value;
            return UseAzureStorage(options)
                ? sp.GetRequiredService<AzureSubjectRepository>()
                : sp.GetRequiredService<InMemorySubjectRepository>();
        });

        services.AddSingleton<AzureSubjectRevisionEventRepository>();
        services.AddSingleton<InMemorySubjectRevisionEventRepository>();
        services.AddSingleton<ISubjectRevisionEventRepository>(sp =>
            UseAzureStorage(sp.GetRequiredService<IOptions<AzureStorageOptions>>().Value)
                ? sp.GetRequiredService<AzureSubjectRevisionEventRepository>()
                : sp.GetRequiredService<InMemorySubjectRevisionEventRepository>());

        // Stat-cam stores: Azure when storage is configured, in-memory otherwise.
        AddStore<ISessionRepository, AzureSessionRepository, InMemorySessionRepository>(services);
        AddStore<ISensingLog, AzureSensingLog, InMemorySensingLog>(services);
        AddStore<IIngestLedger, AzureIngestLedger, InMemoryIngestLedger>(services);
        AddStore<IRollupStore, AzureRollupStore, InMemoryRollupStore>(services);
        AddStore<IAchievementStore, AzureAchievementStore, InMemoryAchievementStore>(services);

        // Idempotency cache for ingest retries. 10-minute TTL is the load-bearing product
        // promise: long enough to span a WiFi blip, short enough to keep the dictionary bounded.
        services.AddSingleton<IIdempotencyCache>(sp => new InMemoryIdempotencyCache(
            ttl: TimeSpan.FromMinutes(10),
            logger: sp.GetRequiredService<ILogger<InMemoryIdempotencyCache>>()));

        services.AddSingleton<IObservationProcessingGate, InMemoryObservationProcessingGate>();
        services.AddSingleton<IDiagnosticsProvider, LocalDiagnosticsProvider>();
        services.AddSingleton<ITelemetryContentSanitizer, TelemetryContentSanitizer>();

        // Runs once before the app accepts requests: creates tables/containers and seeds slug registry.
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
