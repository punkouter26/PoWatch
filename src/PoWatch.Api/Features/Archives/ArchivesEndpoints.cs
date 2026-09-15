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

        group.MapGet("/{date}/handoff-report", async (
            string date,
            string? shiftWindow,
            ReportService reportService,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            if (!DateOnly.TryParse(date, out var parsedDate))
            {
                return Results.BadRequest(new { message = "Date must be in ISO format (yyyy-MM-dd)." });
            }

            if (!Enum.TryParse<ShiftWindow>(shiftWindow ?? "FullDay", ignoreCase: true, out var shift))
            {
                return Results.BadRequest(new { message = "shiftWindow must be one of: FullDay, Morning, Afternoon, Night." });
            }

            logger.LogInformation(
                "Handoff report requested. Date={Date} ShiftWindow={ShiftWindow}",
                parsedDate,
                shift);

            var report = await reportService.BuildHandoffReportAsync(parsedDate, shift, cancellationToken);

            byte[] pdfBytes;
            try
            {
                pdfBytes = HandoffReportRenderer.Render(report);
            }
            catch (HandoffReportRenderer.RendererUnavailableException ex)
            {
                // Explain it rather than returning a bare 500 the operator cannot act on.
                logger.LogError(ex, "Handoff report renderer unavailable. Date={Date} ShiftWindow={ShiftWindow}", parsedDate, shift);
                return Results.Problem(
                    title: "The PDF report could not be generated on this server.",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            logger.LogInformation(
                "Handoff report rendered. Date={Date} ShiftWindow={ShiftWindow} SizeBytes={Size}",
                parsedDate,
                shift,
                pdfBytes.Length);

            return Results.File(
                pdfBytes,
                contentType: "application/pdf",
                fileDownloadName: $"PoWatch-Handoff-{parsedDate:yyyy-MM-dd}-{shift}.pdf");
        })
        .WithName("ArchivesHandoffReport")
        .WithSummary("Generate a PDF shift handoff report for a given date and shift window.")
        .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
        .Produces(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/{date}/handoff-brief", async (
            string date,
            GenerateHandoffBriefRequestDto request,
            HandoffCoachService handoffCoachService,
            IOptions<FeatureFlagsOptions> flags,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            if (!flags.Value.HandoffCoachEnabled)
            {
                logger.LogInformation("Handoff Coach endpoint requested but HandoffCoachEnabled is false.");
                return Results.StatusCode(503);
            }

            if (!DateOnly.TryParse(date, out var parsedDate))
                return Results.BadRequest(new { message = "Date must be in ISO format (yyyy-MM-dd)." });

            logger.LogInformation(
                "Handoff Coach brief requested. Date={Date} ShiftWindow={ShiftWindow} Audience={Audience}",
                parsedDate, request.ShiftWindow, request.Audience);

            var brief = await handoffCoachService.GenerateBriefAsync(parsedDate, request, cancellationToken);
            return Results.Ok(brief);
        })
        .WithName("ArchivesHandoffBrief")
        .WithSummary("Generate a Handoff Coach brief (AI-assisted or template) for a given date.")
        .Produces<HandoffBriefDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status503ServiceUnavailable);

        // Blob (significant-image SAS) routes belong to the Archives slice.
        app.MapBlobEndpoints();

        return app;
    }
}
