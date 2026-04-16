using System.Diagnostics;
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

        return app;
    }
}
