using Microsoft.Extensions.DependencyInjection;
using PoWatch.Application.Services;

namespace PoWatch.Application.Features.Observer;

public static class ObserverFeatureRegistration
{
    public static IServiceCollection AddObserverFeature(this IServiceCollection services)
    {
        services.AddScoped<ObservationService>();
        return services;
    }
}
