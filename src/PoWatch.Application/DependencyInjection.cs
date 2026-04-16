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

        return services;
    }
}
