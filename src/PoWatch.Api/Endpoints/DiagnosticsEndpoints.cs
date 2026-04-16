using System.Diagnostics;
using PoWatch.Application.Contracts;

namespace PoWatch.Api.Endpoints;

internal static class DiagnosticsEndpoints
{
    internal static IEndpointRouteBuilder MapDiagnosticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/diagnostics").WithTags("Diagnostics");

        group.MapGet("/status", (IDiagnosticsProvider provider, ILogger<Program> logger) =>
        {
            logger.LogDebug(
                "Diagnostics API request received. TraceId={TraceId}",
                Activity.Current?.TraceId.ToString());
            return Results.Ok(provider.CaptureSnapshot());
        })
        .WithName("DiagnosticsStatus")
        .WithSummary("Get the masked system health snapshot for the current environment.");

        return app;
    }
}
