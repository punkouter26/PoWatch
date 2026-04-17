using System.Diagnostics;
using Microsoft.Extensions.Options;
using PoWatch.Application.Options;
using PoWatch.Application.Services;
using PoWatch.Shared.Models;

namespace PoWatch.Api.Endpoints;

internal static class IdentityEndpoints
{
    internal static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/identity").WithTags("Identity");

        group.MapPost("/subjects", async (
            RegisterSubjectRequestDto request,
            IdentityService service,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.DisplayName))
                return Results.BadRequest(new { message = "DisplayName is required." });

            logger.LogInformation(
                "Register known subject API request received. DisplayName={DisplayName} TraceId={TraceId}",
                request.DisplayName,
                Activity.Current?.TraceId.ToString());

            var created = await service.RegisterKnownSubjectAsync(request, cancellationToken);
            return Results.Ok(created);
        })
        .WithName("IdentityRegisterSubject")
        .WithSummary("Pre-register a known subject identity without requiring an observation.");

        group.MapGet("/subjects", async (
            IdentityService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.GetSubjectsAsync(cancellationToken)))
            .WithName("IdentitySubjects")
            .WithSummary("List all known and temporary subject identities.");

        group.MapPatch("/subjects/{subjectId}", async (
            string subjectId,
            RenameSubjectRequestDto request,
            IdentityService service,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.NewName))
            {
                return Results.BadRequest(new { message = "newName is required." });
            }

            logger.LogInformation(
                "Identity rename API request received. SubjectId={SubjectId} TraceId={TraceId}",
                subjectId,
                Activity.Current?.TraceId.ToString());

            var renamed = await service.RenameAsync(subjectId, request, cancellationToken);
            return Results.Ok(renamed);
        })
        .WithName("IdentityRename")
        .WithSummary("Rename a temporary subject and rewrite its historical identity references.");

        group.MapPost("/merge", async (
            MergeIdentityRequestDto request,
            IdentityService service,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.PrimarySubjectId)
                || string.IsNullOrWhiteSpace(request.SecondarySubjectId))
            {
                return Results.BadRequest(new { message = "PrimarySubjectId and SecondarySubjectId are required." });
            }

            logger.LogInformation(
                "Identity merge API request received. PrimarySubjectId={PrimarySubjectId}, SecondarySubjectId={SecondarySubjectId}, TraceId={TraceId}",
                request.PrimarySubjectId,
                request.SecondarySubjectId,
                Activity.Current?.TraceId.ToString());

            var merged = await service.MergeAsync(request, cancellationToken);
            return Results.Ok(merged);
        })
        .WithName("IdentityMerge")
        .WithSummary("Merge two subject identities into one canonical history.");

        group.MapGet("/subjects/live-status", async (
            IdentityService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.GetLiveDashboardStatusAsync(cancellationToken)))
            .WithName("IdentityLiveDashboard")
            .WithSummary("Get live status snapshot for all subjects including today's recent events.");

        group.MapGet("/subjects/live-risk", async (
            DriftRadarService driftRadarService,
            IOptions<FeatureFlagsOptions> flags,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            if (!flags.Value.DriftRadarEnabled)
            {
                logger.LogInformation("Drift Radar endpoint requested but DriftRadarEnabled is false. TraceId={TraceId}", Activity.Current?.TraceId.ToString());
                return Results.StatusCode(503);
            }

            logger.LogInformation("Drift Radar live-risk requested. TraceId={TraceId}", Activity.Current?.TraceId.ToString());
            var status = await driftRadarService.GetDriftStatusAsync(cancellationToken);
            return Results.Ok(status);
        })
        .WithName("DriftRadarLiveRisk")
        .WithSummary("Get Drift Radar status for all subjects — drift score, label, hourly vectors, and insights.");

        group.MapGet("/subjects/{subjectId}/baseline", async (
            string subjectId,
            int? days,
            BaselineService baselineService,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(subjectId))
                return Results.BadRequest(new { message = "subjectId is required." });

            var baselineDays = Math.Clamp(days ?? 7, 1, 90);

            logger.LogInformation(
                "Baseline requested. SubjectId={SubjectId} Days={Days} TraceId={TraceId}",
                subjectId,
                baselineDays,
                Activity.Current?.TraceId.ToString());

            try
            {
                var baseline = await baselineService.GetBaselineAsync(subjectId, cancellationToken, baselineDays);
                return Results.Ok(baseline);
            }
            catch (InvalidOperationException ex)
            {
                logger.LogWarning("Baseline request failed. SubjectId={SubjectId} Reason={Reason}", subjectId, ex.Message);
                return Results.NotFound(new { message = ex.Message });
            }
        })
        .WithName("IdentitySubjectBaseline")
        .WithSummary("Get the 7-day behavioral baseline and drift score for a subject.");

        return app;
    }
}
