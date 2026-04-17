using PoWatch.Api.Endpoints;

namespace PoWatch.Api.Features.Identity;

internal static class IdentityFeatureRoutes
{
    internal static IEndpointRouteBuilder MapIdentityFeature(this IEndpointRouteBuilder app) =>
        app.MapIdentityEndpoints();
}
