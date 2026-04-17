using PoWatch.Api.Endpoints;

namespace PoWatch.Api.Features.Diagnostics;

internal static class DiagnosticsFeatureRoutes
{
    internal static IEndpointRouteBuilder MapDiagnosticsFeature(this IEndpointRouteBuilder app) =>
        app.MapDiagnosticsEndpoints();
}
