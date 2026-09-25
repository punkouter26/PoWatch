using PoWatch.Api.Infrastructure;
using PoWatch.Application.Mappers;
using PoWatch.Application.Options;
using PoWatch.Application.Services;
using PoWatch.Domain.Models;
using PoWatch.Shared.Models;
using Microsoft.Extensions.Options;

namespace PoWatch.Api.Features.Archives;

internal static class ArchivesEndpoints
{
    /// <summary>Domain → DTO mapping for the daily chapter endpoint. Lives here (not in the
    /// application mappers) because it only converts the Shared-side enums, and pulling it into
    /// a shared mapper would force PoWatch.Application to know about the Shared DTO shape, which
    /// the architecture-boundary tests in <c>PoWatch.Unit</c> would rightly fail.</summary>
    private static DailyChapterDto ToDto(DailyChapter chapter) => new()
    {
        Date = chapter.Date,
        Timeline = chapter.Timeline.ToDtos(),
        Highlights = chapter.Highlights.ToDtos(),
        ClinicalNarrative = chapter.ClinicalNarrative,
        StructuredRows = chapter.StructuredRows
            .Select(r => new StructuredNarrativeRowDto
            {
                ObservedAtUtcLocal = r.ObservedAtUtcLocal,
                SubjectDisplayName = r.SubjectDisplayName,
                Activity = r.Activity,
                Level = r.Level.ToShared(),
                SignificantReason = r.SignificantReason
            })
            .ToList(),
        Mode = chapter.Mode switch
        {
            PoWatch.Domain.Services.ActivitySignificanceNarrativeMode.Structured => NarrativeMode.Structured,
            _ => NarrativeMode.Prose
        },
        TotalEvents = chapter.TotalEvents,
        OutlierCount = chapter.OutlierCount,
        NotableCount = chapter.NotableCount,
        SubjectCount = chapter.SubjectCount,
        FirstEventUtc = chapter.FirstEventUtc,
        LastEventUtc = chapter.LastEventUtc
    };

    internal static IEndpointRouteBuilder MapArchivesFeature(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/archives").WithTags("Archives").RequireAuthorization();

        group.MapGet("/{date}", async (
            string date,
            string? mode,
            ArchivesService service,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            if (!DateOnly.TryParse(date, out var parsedDate))
            {
                return Results.BadRequest(new { message = "Date must be in ISO format (yyyy-MM-dd)." });
            }

            // Default to Prose so older clients that never knew about the mode parameter keep
            // getting the same single-paragraph narrative they always did. An unknown value is
            // rejected explicitly — better than silently falling through to the default.
            NarrativeMode parsedMode = NarrativeMode.Prose;
            if (!string.IsNullOrWhiteSpace(mode))
            {
                if (!Enum.TryParse(mode, ignoreCase: true, out parsedMode))
                {
                    return Results.BadRequest(new { message = "mode must be one of: Prose, Structured." });
                }
            }

            try
            {
                var chapter = await service.GetChapterAsync(parsedDate, parsedMode, cancellationToken);
                return Results.Ok(ToDto(chapter));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to load daily chapter. Date={Date} Mode={Mode}", parsedDate, parsedMode);
                return Results.Problem(
                    title: "Failed to load chapter",
                    detail: ex.Message,
                    statusCode: 500);
            }
        })
        .WithName("ArchivesGetChapter")
        .WithSummary("Get the daily chapter narrative, timeline, and highlights for a date.")
        .Produces<DailyChapterDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status500InternalServerError);

        // Blob (significant-image SAS) routes belong to the Archives slice.
        app.MapBlobEndpoints();

        return app;
    }
}
