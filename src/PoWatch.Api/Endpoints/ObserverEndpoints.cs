using System.Diagnostics;
using System.Runtime.CompilerServices;
using PoWatch.Application.Services;
using PoWatch.Shared.Models;

namespace PoWatch.Api.Endpoints;

internal static class ObserverEndpoints
{
    internal static IEndpointRouteBuilder MapObserverEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/observer").WithTags("Observer");

        group.MapPost("/ingest", async (
            IngestObservationRequestDto request,
            ObservationService service,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            logger.LogInformation(
                "Observer ingest API request received. Activity={Activity} SubjectHint={SubjectHint} TraceId={TraceId}",
                request.Activity,
                request.SubjectHint,
                Activity.Current?.TraceId.ToString());

            var result = await service.IngestAsync(request, cancellationToken);

            logger.LogInformation(
                "Observer ingest API request completed. Accepted={Accepted} Dropped={Dropped} SubjectId={SubjectId} TraceId={TraceId}",
                result.Accepted,
                result.Dropped,
                result.SubjectId,
                Activity.Current?.TraceId.ToString());

            return result.Dropped ? Results.Accepted(value: result) : Results.Ok(result);
        })
        .WithName("ObserverIngest")
        .WithSummary("Persist a locally inferred observation event.");

        group.MapGet("/state", (ObservationService service) =>
            Results.Ok(service.GetRuntimeState()))
            .WithName("ObserverState")
            .WithSummary("Get the live observer runtime status and feature flags.");

        // SSE streaming — pushes new observations to subscribed clients in real time
        group.MapGet("/events", (
            [Microsoft.AspNetCore.Mvc.FromQuery] DateTimeOffset? since,
            ArchivesService archivesService,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("PoWatch.ObserverEventStream");
            var cursor = since ?? DateTimeOffset.UtcNow.AddMinutes(-5);
            logger.LogDebug("SSE stream opened. Cursor={Cursor}", cursor);
            return TypedResults.ServerSentEvents(PollAsync(archivesService, cursor, logger, ct));
        })
        .WithName("ObserverEventStream")
        .WithSummary("Subscribe to a real-time SSE stream of observation events.");

        return app;
    }

    private static async IAsyncEnumerable<ObservationEventDto> PollAsync(
        ArchivesService archivesService,
        DateTimeOffset cursor,
        ILogger logger,
        [EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            IReadOnlyList<ObservationEventDto> entries;
            try
            {
                var date = DateOnly.FromDateTime(cursor.UtcDateTime);
                var chapter = await archivesService.GetChapterAsync(date, ct).ConfigureAwait(false);
                entries = [.. chapter.Timeline
                    .Where(e => e.ObservedAtUtc > cursor)
                    .OrderBy(e => e.ObservedAtUtc)
                    .Select(e => new ObservationEventDto
                    {
                        Id = e.Id,
                        ObservedAtUtc = e.ObservedAtUtc,
                        SubjectId = e.SubjectId,
                        SubjectDisplayName = e.SubjectDisplayName,
                        Activity = e.Activity,
                        ClinicalDescription = e.ClinicalDescription,
                        IsSignificant = e.IsSignificant,
                        SignificantReason = e.SignificantReason,
                        IsClinicalOutlier = e.IsClinicalOutlier,
                        ImageReference = e.ImageReference
                    })];
            }
            catch (OperationCanceledException)
            {
                yield break;
            }

            foreach (var entry in entries)
            {
                cursor = entry.ObservedAtUtc;
                yield return entry;
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                logger.LogDebug("SSE stream closed. Cursor={Cursor}", cursor);
                yield break;
            }
        }
    }
}

