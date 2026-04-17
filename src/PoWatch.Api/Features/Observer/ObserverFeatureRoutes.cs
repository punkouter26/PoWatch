using PoWatch.Api.Endpoints;

namespace PoWatch.Api.Features.Observer;

internal static class ObserverFeatureRoutes
{
    internal static IEndpointRouteBuilder MapObserverFeature(this IEndpointRouteBuilder app) =>
        app.MapObserverEndpoints();
}
