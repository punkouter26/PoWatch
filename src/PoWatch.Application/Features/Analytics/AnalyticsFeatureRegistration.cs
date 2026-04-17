using Microsoft.Extensions.DependencyInjection;
using PoWatch.Application.Services;

namespace PoWatch.Application.Features.Analytics;

public static class AnalyticsFeatureRegistration
{
    public static IServiceCollection AddAnalyticsFeature(this IServiceCollection services)
    {
        services.AddScoped<BaselineService>();
        services.AddScoped<DriftRadarService>();
        return services;
    }
}
