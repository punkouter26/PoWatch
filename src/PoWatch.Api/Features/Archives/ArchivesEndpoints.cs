using PoWatch.Api.Infrastructure;
using PoWatch.Application.Options;
using PoWatch.Application.Services;
using PoWatch.Shared.Models;
using Microsoft.Extensions.Options;

namespace PoWatch.Api.Features.Archives;

internal static class ArchivesEndpoints
{
    internal static IEndpointRouteBuilder MapArchivesFeature(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/archives").WithTags("Archives").RequireAuthorization();

        group.MapGet("/{date}", async (
            string date,
            ArchivesService service,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            if (!DateOnly.TryParse(date, out var parsedDate))
            {
                return Results.BadRequest(new { message = "Date must be in ISO format (yyyy-MM-dd)." });
            }

            try
            {
                var chapter = await service.GetChapterAsync(parsedDate, cancellationToken);
                return Results.Ok(chapter);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to load daily chapter. Date={Date}", parsedDate);
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
