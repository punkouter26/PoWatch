using Microsoft.Extensions.DependencyInjection;
using PoWatch.Application.Services;

namespace PoWatch.Application.Features.Fhir;

public static class FhirFeatureRegistration
{
    public static IServiceCollection AddFhirFeature(this IServiceCollection services)
    {
        services.AddScoped<FhirMappingService>();
        return services;
    }
}
