using PoWatch.Api.Endpoints;

namespace PoWatch.Api.Features.Fhir;

internal static class FhirFeatureRoutes
{
    internal static IEndpointRouteBuilder MapFhirFeature(this IEndpointRouteBuilder app) =>
        app.MapFhirEndpoints();
}
