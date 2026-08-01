using System.Diagnostics;
using System.Runtime.CompilerServices;
using PoWatch.Application.Contracts;
using PoWatch.Application.Services;
using PoWatch.Domain.Models;
using PoWatch.Shared.Models;
using Microsoft.Extensions.Caching.Hybrid;

namespace PoWatch.Api.Features.Observer;

internal static class ObserverEndpoints
{
    internal static IEndpointRouteBuilder MapObserverFeature(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/observer").WithTags("Observer").RequireAuthorization();

        group.MapPost("/ingest", async (
            IngestObservationRequestDto request,
            ObservationService service,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            ObserverLog.IngestReceived(logger, request.Activity, request.SubjectHint);

            var result = await service.IngestAsync(request, cancellationToken);

            ObserverLog.IngestCompleted(logger, result.Accepted, result.Dropped, result.SubjectId);

            return result.Dropped ? Results.Accepted(value: result) : Results.Ok(result);
        })
        .WithName("ObserverIngest")
        .WithSummary("Persist a locally inferred observation event.")
        .Produces<IngestObservationResultDto>(StatusCodes.Status200OK)
        .Produces<IngestObservationResultDto>(StatusCodes.Status202Accepted);

        group.MapGet("/state", (
            ObservationService service,
            ILogger<Program> logger) =>
        {
            try
            {
                return Results.Ok(service.GetRuntimeState());
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Observer runtime state retrieval failed. TraceId={TraceId}",
                    Activity.Current?.TraceId.ToString());

                return Results.Problem(
                    title: "Unable to load observer runtime state.",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError,
                    extensions: new Dictionary<string, object?>
                    {
                        ["traceId"] = Activity.Current?.TraceId.ToString()
                    });
            }
        })
            .WithName("ObserverState")
            .WithSummary("Get the live observer runtime status and feature flags.")
            .Produces<ObserverRuntimeStateDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // SSE streaming with backpressure support
        group.MapGet("/events", (
            [Microsoft.AspNetCore.Mvc.FromQuery] DateTimeOffset? since,
            [Microsoft.AspNetCore.Mvc.FromQuery] int? batchSize,
            IObservationRepository observationRepository,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("PoWatch.ObserverEventStream");
            var cursor = since ?? DateTimeOffset.UtcNow.AddMinutes(-5);
            var maxBatchSize = Math.Min(batchSize ?? 50, 500); // Cap at 500 events per batch

            logger.LogDebug("SSE stream opened. Cursor={Cursor} BatchSize={BatchSize}", cursor, maxBatchSize);

            return TypedResults.ServerSentEvents(PsePollAsync(observationRepository, cursor, maxBatchSize, logger, ct));
        })
        .WithName("ObserverEventStream")
        .WithSummary("Subscribe to a real-time SSE stream of observation events with backpressure support.");

        // Acknowledgment endpoint for significant events
        group.MapPost("/acknowledge", async (
            AcknowledgeEventsRequestDto request,
            IAcknowledgementRegistry acknowledgementRegistry,
            HybridCache cache,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            // Transport gives us strings; adopt them as event ids here and drop anything malformed.
            var parsed = request.EventIds
                .Select(ObservationEventId.Parse)
                .Where(id => !id.IsEmpty)
                .ToList();

            acknowledgementRegistry.Acknowledge(parsed, request.AcknowledgedBy);

            // The live-status board is cached for ~10 s under a single key. Without this eviction,
            // acknowledging an alert left its badge on screen until the entry expired — the operator
            // pressed the button and nothing appeared to happen, so they pressed it again.
            await cache.RemoveAsync(IdentityCacheKeys.LiveStatus, ct);

            logger.LogInformation(
                "Events acknowledged. EventIds={Count} AcknowledgedBy={AcknowledgedBy}",
                parsed.Count,
                request.AcknowledgedBy);

            return TypedResults.Ok(new AcknowledgeEventsResultDto(parsed.Count, DateTimeOffset.UtcNow));
        })
        .WithName("ObserverAcknowledge")
        .WithSummary("Acknowledge one or more significant events to mark them as reviewed.");

        return app;
    }

    /// <summary>
    /// SSE poll with backpressure-aware batching.
    /// Respects the batchSize parameter to prevent overwhelming slow clients.
    /// Automatically adjusts poll interval based on client consumption rate.
    /// </summary>
    private static async IAsyncEnumerable<ObservationEventDto> PsePollAsync(
        IObservationRepository observationRepository,
        DateTimeOffset cursor,
        int maxBatchSize,
        ILogger logger,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var basePollIntervalMs = 3000; // 3 seconds base
        var consecutiveEmptyPolls = 0;
        var maxPollIntervalMs = 15000; // Max 15 seconds between polls

        while (!ct.IsCancellationRequested)
        {
            IReadOnlyList<ObservationEventDto> entries;
            try
            {
                var date = DateOnly.FromDateTime(cursor.UtcDateTime);
                var events = await observationRepository.GetByDateAsync(date, ct).ConfigureAwait(false);

                // GetByDateAsync already returns events sorted ascending by ObservedAtUtc (both the Azure
                // and in-memory repos), so the previous second .OrderBy here was redundant work repeated
                // every 3s per connected client. Filter by cursor and cap only.
                entries = events
                    .Where(e => e.ObservedAtUtc > cursor)
                    .Take(maxBatchSize) // Respect batch size limit
                    .Select(e => new ObservationEventDto
                    {
                        Id = (Guid)e.Id,
                        ObservedAtUtc = e.ObservedAtUtc,
                        SubjectId = e.SubjectId,
                        SubjectDisplayName = e.SubjectDisplayName,
                        Activity = e.Activity,
                        ClinicalDescription = e.ClinicalDescription,
                        IsSignificant = e.IsSignificant,
                        SignificantReason = e.SignificantReason,
                        IsClinicalOutlier = e.IsClinicalOutlier,
                        ImageReference = e.ImageReference
                    })
                    .ToList();

                consecutiveEmptyPolls = entries.Count == 0 ? consecutiveEmptyPolls + 1 : 0;
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

            // Adaptive poll interval: increase delay when no events, decrease when events are flowing
            var pollIntervalMs = consecutiveEmptyPolls > 0
                ? Math.Min(basePollIntervalMs * (consecutiveEmptyPolls + 1), maxPollIntervalMs)
                : basePollIntervalMs;

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(pollIntervalMs), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                logger.LogDebug("SSE stream closed. Cursor={Cursor} TotalEventsSent={Total}", cursor, entries.Count);
                yield break;
            }
        }
    }
}
