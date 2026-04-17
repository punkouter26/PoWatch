using Microsoft.Extensions.DependencyInjection;
using PoWatch.Application.Services;

namespace PoWatch.Application.Features.Archives;

public static class ArchivesFeatureRegistration
{
    public static IServiceCollection AddArchivesFeature(this IServiceCollection services)
    {
        services.AddScoped<ArchivesService>();
        services.AddScoped<ReportService>();
        services.AddScoped<HandoffCoachService>();
        return services;
    }
}
