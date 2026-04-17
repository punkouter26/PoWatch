using Microsoft.Extensions.DependencyInjection;
using PoWatch.Application.Features.Analytics;
using PoWatch.Application.Features.Archives;
using PoWatch.Application.Features.Fhir;
using PoWatch.Application.Features.Identity;
using PoWatch.Application.Features.Observer;
using PoWatch.Application.Features.Risk;
using PoWatch.Application.Services;

namespace PoWatch.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddPoWatchApplication(this IServiceCollection services)
    {
        services.AddObserverFeature();
        services.AddArchivesFeature();
        services.AddIdentityFeature();
        services.AddAnalyticsFeature();
        services.AddFhirFeature();
        services.AddRiskFeature();

        return services;
    }
}
