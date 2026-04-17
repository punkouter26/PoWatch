using PoWatch.Api.Endpoints;

namespace PoWatch.Api.Features.Archives;

internal static class ArchivesFeatureRoutes
{
    internal static IEndpointRouteBuilder MapArchivesFeature(this IEndpointRouteBuilder app)
    {
        app.MapArchivesEndpoints();
        app.MapBlobEndpoints();
        return app;
    }
}
