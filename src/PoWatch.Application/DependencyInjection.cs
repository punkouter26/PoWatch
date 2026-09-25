using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PoWatch.Application.Services;

namespace PoWatch.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddPoWatchApplication(this IServiceCollection services)
    {
        // Sessions
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<SessionService>();
        services.AddScoped<IngestService>();
        services.AddScoped<StatsQueryService>();
        services.AddScoped<RegularsService>();
        services.AddScoped<RecapService>();
        services.AddScoped<AchievementService>();

        return services;
    }
}
