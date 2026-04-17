using Microsoft.Extensions.DependencyInjection;
using PoWatch.Application.Services;

namespace PoWatch.Application.Features.Risk;

public static class RiskFeatureRegistration
{
    public static IServiceCollection AddRiskFeature(this IServiceCollection services)
    {
        services.AddSingleton<AlertThresholdEvaluator>();
        return services;
    }
}
