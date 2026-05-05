using Microsoft.Extensions.DependencyInjection;
using PoWatch.Application.Services;

namespace PoWatch.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddPoWatchApplication(this IServiceCollection services)
    {
        // Analytics
        services.AddScoped<DriftRadarService>();

        // Archives
        services.AddScoped<ArchivesService>();
        services.AddScoped<ReportService>();
        services.AddScoped<HandoffCoachService>();

        // FHIR
        services.AddScoped<FhirMappingService>();

        // Identity
        services.AddScoped<IdentityService>();

        // Observer
        services.AddScoped<ObservationService>();

        // Risk
        services.AddSingleton<AlertThresholdEvaluator>();

        return services;
    }
}
