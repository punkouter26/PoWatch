using System.Diagnostics;
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

        // NOTE: the SSE endpoint (GET /api/observer/events) was removed — no client, page, or test
        // ever consumed it. The Live Room refreshes via per-cycle ingest responses plus explicit
        // timeline/subject re-fetches, so the stream was a dead transport polling Table Storage
        // every 3s per hypothetical subscriber.

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
}
