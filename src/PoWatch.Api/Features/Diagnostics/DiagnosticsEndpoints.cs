using System.Diagnostics;
using Microsoft.Extensions.Options;
using PoWatch.Application.Contracts;
using PoWatch.Application.Options;

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

        group.MapPost("/reset", async (
            IStorageResetService resetService,
            IOptions<FeatureFlagsOptions> flags,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            if (!flags.Value.AllowDataReset)
            {
                logger.LogWarning("Storage reset attempted but AllowDataReset feature flag is disabled.");
                return Results.NotFound();
            }

            logger.LogWarning(
                "Storage reset endpoint invoked. TraceId={TraceId}",
                Activity.Current?.TraceId.ToString());

            var result = await resetService.ResetAllAsync(cancellationToken);

            return result.Success
                ? Results.Ok(result)
                : Results.Problem(result.ErrorMessage, statusCode: 500);
        })
        .WithName("DiagnosticsReset")
        .WithSummary("Deletes all observations, subjects, and blobs. Requires AllowDataReset feature flag. Never expose in production.");

        return app;
    }
}
