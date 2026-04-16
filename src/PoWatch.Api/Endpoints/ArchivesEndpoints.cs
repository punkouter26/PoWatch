using PoWatch.Application.Services;

namespace PoWatch.Api.Endpoints;

internal static class ArchivesEndpoints
{
    internal static IEndpointRouteBuilder MapArchivesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/archives").WithTags("Archives");

        group.MapGet("/{date}", async (
            string date,
            ArchivesService service,
            CancellationToken cancellationToken) =>
        {
            if (!DateOnly.TryParse(date, out var parsedDate))
            {
                return Results.BadRequest(new { message = "Date must be in ISO format (yyyy-MM-dd)." });
            }

            var chapter = await service.GetChapterAsync(parsedDate, cancellationToken);
            return Results.Ok(chapter);
        })
        .WithName("ArchivesGetChapter")
        .WithSummary("Get the daily chapter narrative, timeline, and highlights for a date.");

        return app;
    }
}
