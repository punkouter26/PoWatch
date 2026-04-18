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
        // Named HttpClient for Azure OpenAI — proper lifetime, DNS refresh, and disposal via IHttpClientFactory
        services.AddHttpClient("AzureOpenAi", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(45);
        });
        // Handoff Coach summarizer — template is always registered; Azure OpenAI used when configured
        services.AddScoped<TemplateHandoffSummarizer>();
        services.AddScoped<AzureOpenAiHandoffSummarizer>();
        services.AddScoped<IHandoffSummarizer>(sp =>
        {
            var flags = sp.GetRequiredService<IOptions<FeatureFlagsOptions>>().Value;
            var openAiOptions = sp.GetRequiredService<IOptions<AzureOpenAiOptions>>().Value;
            return flags.AzureOpenAiEnabled && !string.IsNullOrWhiteSpace(openAiOptions.Endpoint)
                ? sp.GetRequiredService<AzureOpenAiHandoffSummarizer>()
                : sp.GetRequiredService<TemplateHandoffSummarizer>();
        });

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

        services.AddSingleton<IObservationProcessingGate, InMemoryObservationProcessingGate>();
        services.AddSingleton<IDiagnosticsProvider, LocalDiagnosticsProvider>();
        services.AddSingleton<ITelemetryContentSanitizer, TelemetryContentSanitizer>();
        services.AddSingleton<IAcknowledgementRegistry, InMemoryAcknowledgementRegistry>();

        // Runs once before the app accepts requests: creates tables/containers and seeds slug registry.
        services.AddHostedService<AzureStorageInitializer>();

        return services;
    }

    private static bool UseAzureStorage(AzureStorageOptions options) =>
        !string.IsNullOrWhiteSpace(options.ConnectionString)
        || !string.IsNullOrWhiteSpace(options.ServiceUri);
}
