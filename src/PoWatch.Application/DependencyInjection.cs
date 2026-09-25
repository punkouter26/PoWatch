using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

        // Identity
        services.AddScoped<IdentityService>();

        // Observer
        services.AddScoped<ObservationService>();

        // Sessions
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<SessionService>();
        services.AddScoped<IngestService>();
        services.AddScoped<StatsQueryService>();

        return services;
    }
}
