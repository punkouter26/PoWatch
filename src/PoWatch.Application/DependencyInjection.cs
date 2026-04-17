using Microsoft.Extensions.DependencyInjection;
using PoWatch.Application.Services;

namespace PoWatch.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddPoWatchApplication(this IServiceCollection services)
    {
        services.AddScoped<ObservationService>();
        services.AddScoped<ArchivesService>();
        services.AddScoped<IdentityService>();
        services.AddScoped<ReportService>();
        services.AddScoped<BaselineService>();
        services.AddScoped<FhirMappingService>();
        services.AddScoped<DriftRadarService>();
        services.AddScoped<HandoffCoachService>();
        // Singleton so the rolling event window survives across request scopes
        services.AddSingleton<AlertThresholdEvaluator>();

        return services;
    }
}
