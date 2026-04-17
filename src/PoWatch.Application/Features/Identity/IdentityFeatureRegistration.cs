using Microsoft.Extensions.DependencyInjection;
using PoWatch.Application.Services;

namespace PoWatch.Application.Features.Identity;

public static class IdentityFeatureRegistration
{
    public static IServiceCollection AddIdentityFeature(this IServiceCollection services)
    {
        services.AddScoped<IdentityService>();
        return services;
    }
}
