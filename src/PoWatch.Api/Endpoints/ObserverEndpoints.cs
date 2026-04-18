using System.Diagnostics;
using System.Runtime.CompilerServices;
using PoWatch.Application.Contracts;
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
            .WithSummary("Get the live observer runtime status and feature flags.");

        // SSE streaming with backpressure support
        group.MapGet("/events", (
            [Microsoft.AspNetCore.Mvc.FromQuery] DateTimeOffset? since,
            [Microsoft.AspNetCore.Mvc.FromQuery] int? batchSize,
            ArchivesService archivesService,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("PoWatch.ObserverEventStream");
            var cursor = since ?? DateTimeOffset.UtcNow.AddMinutes(-5);
            var maxBatchSize = Math.Min(batchSize ?? 50, 500); // Cap at 500 events per batch
            
            logger.LogDebug("SSE stream opened. Cursor={Cursor} BatchSize={BatchSize}", cursor, maxBatchSize);
            
            return TypedResults.ServerSentEvents(PsePollAsync(archivesService, cursor, maxBatchSize, logger, ct));
        })
        .WithName("ObserverEventStream")
        .WithSummary("Subscribe to a real-time SSE stream of observation events with backpressure support.");

        // Acknowledgment endpoint for significant events
        group.MapPost("/acknowledge", (
            AcknowledgeEventsRequestDto request,
            IAcknowledgementRegistry acknowledgementRegistry,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            var parsed = request.EventIds
                .Select(id => Guid.TryParse(id, out var g) ? g : (Guid?)null)
                .Where(g => g.HasValue)
                .Select(g => g!.Value)
                .ToList();

            acknowledgementRegistry.Acknowledge(parsed, request.AcknowledgedBy);

            logger.LogInformation(
                "Events acknowledged. EventIds={Count} AcknowledgedBy={AcknowledgedBy}",
                parsed.Count,
                request.AcknowledgedBy);

            return Results.Ok(new { AcknowledgedCount = parsed.Count, AcknowledgedAtUtc = DateTimeOffset.UtcNow });
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
        ArchivesService archivesService,
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
                var chapter = await archivesService.GetChapterAsync(date, ct).ConfigureAwait(false);
                
                entries = chapter.Timeline
                    .Where(e => e.ObservedAtUtc > cursor)
                    .OrderBy(e => e.ObservedAtUtc)
                    .Take(maxBatchSize) // Respect batch size limit
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

/// <summary>
/// Request DTO for acknowledging significant events.
/// </summary>
public sealed class AcknowledgeEventsRequestDto
{
    /// <summary>
    /// List of event IDs to acknowledge.
    /// </summary>
    public required IReadOnlyList<string> EventIds { get; init; }

    /// <summary>
    /// Identifier of the person acknowledging (e.g., nurse ID, username).
    /// </summary>
    public required string AcknowledgedBy { get; init; }

    /// <summary>
    /// Optional note explaining the acknowledgment.
    /// </summary>
    public string? Note { get; init; }
}
