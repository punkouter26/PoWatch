using System.Diagnostics;
using PoWatch.Application.Contracts;
using PoWatch.Shared.Models;

namespace PoWatch.Api.Features.Diagnostics;

internal static class DiagnosticsEndpoints
{
    internal static IEndpointRouteBuilder MapDiagnosticsFeature(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/diagnostics").WithTags("Diagnostics").RequireAuthorization();

        group.MapGet("/status", (IDiagnosticsProvider provider, ILogger<Program> logger) =>
        {
            logger.LogDebug(
                "Diagnostics API request received. TraceId={TraceId}",
                Activity.Current?.TraceId.ToString());
            return Results.Ok(provider.CaptureSnapshot());
        })
        .WithName("DiagnosticsStatus")
        .WithSummary("Get the masked system health snapshot for the current environment.")
        .Produces<DiagnosticsSnapshotDto>(StatusCodes.Status200OK);

        return app;
    }
}
