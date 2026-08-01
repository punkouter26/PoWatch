using System.Diagnostics;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;
using PoWatch.Application.Contracts;
using PoWatch.Application.Options;
using PoWatch.Application.Services;
using PoWatch.Shared.Models;

namespace PoWatch.Api.Features.Identity;

internal static class IdentityEndpoints
{
    internal static IEndpointRouteBuilder MapIdentityFeature(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/identity").WithTags("Identity").RequireAuthorization();

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
        .WithSummary("Pre-register a known subject identity without requiring an observation.")
        .Produces<SubjectProfileDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);

        group.MapGet("/subjects", async (
            IdentityService service,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await service.GetSubjectsAsync(cancellationToken));
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Identity subjects retrieval failed. TraceId={TraceId}",
                    Activity.Current?.TraceId.ToString());

                return Results.Problem(
                    title: "Unable to load identity subjects.",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError,
                    extensions: new Dictionary<string, object?>
                    {
                        ["traceId"] = Activity.Current?.TraceId.ToString()
                    });
            }
        })
            .WithName("IdentitySubjects")
            .WithSummary("List all known and temporary subject identities.")
            .Produces<List<SubjectProfileDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

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
        .WithSummary("Rename a temporary subject and rewrite its historical identity references.")
        .Produces<IdentityRevisionResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);

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
        .WithSummary("Merge two subject identities into one canonical history.")
        .Produces<IdentityRevisionResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);

        group.MapGet("/subjects/live-status", async (
            IdentityService service,
            HybridCache cache,
            CancellationToken cancellationToken) =>
            Results.Ok(await cache.GetOrCreateAsync(
                IdentityCacheKeys.LiveStatus,
                async ct => await service.GetLiveDashboardStatusAsync(ct),
                cancellationToken: cancellationToken)))
            .WithName("IdentityLiveDashboard")
            .WithSummary("Get live status snapshot for all subjects including today's recent events (cached ~10s).")
            .Produces<List<SubjectLiveStatusDto>>(StatusCodes.Status200OK);

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
        .WithSummary("Get Drift Radar status for all subjects — drift score, label, hourly vectors, and insights.")
        .Produces<List<SubjectDriftStatusDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/subjects/{subjectId}/baseline", async (
            string subjectId,
            int? days,
            IObservationRepository observationRepository,
            ISubjectRepository subjectRepository,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(subjectId))
                return Results.BadRequest(new { message = "subjectId is required." });

            var baselineDays = Math.Clamp(days ?? 7, 1, 90);
            logger.LogInformation(
                "Baseline requested. SubjectId={SubjectId} Days={Days} TraceId={TraceId}",
                subjectId, baselineDays, Activity.Current?.TraceId.ToString());

            var subject = await subjectRepository.GetByIdAsync(subjectId, cancellationToken);
            if (subject is null)
                return Results.NotFound(new { message = $"Subject '{subjectId}' was not found." });

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var historicalEvents = await observationRepository.GetBySubjectAndDateRangeAsync(
                subjectId, today.AddDays(-baselineDays), today.AddDays(-1), cancellationToken);
            var todayAll = await observationRepository.GetByDateAsync(today, cancellationToken);
            var todayEvents = todayAll
                .Where(e => string.Equals(e.SubjectId, subjectId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var localOffset = TimeZoneInfo.Local.GetUtcOffset(today.ToDateTime(TimeOnly.MinValue));
            var baselineVector = DriftMath.BuildHourlyVector(historicalEvents, localOffset);
            var todayVector = DriftMath.BuildHourlyVector(todayEvents, localOffset);
            var driftScore = DriftMath.ComputeDriftScore(baselineVector, todayVector);
            var driftLabel = DriftMath.ClassifyDrift(driftScore);

            logger.LogInformation(
                "Baseline computed. SubjectId={SubjectId} Historical={Historical} Today={Today} DriftScore={Drift:F1} Label={Label}",
                subjectId, historicalEvents.Count, todayEvents.Count, driftScore, driftLabel);

            return Results.Ok(new SubjectBaselineDto
            {
                SubjectId = subject.SubjectId,
                DisplayName = subject.DisplayName,
                ComputedForDate = today,
                BaselineDays = baselineDays,
                HourlyBaselineVector = baselineVector,
                HourlyTodayVector = todayVector,
                DriftScore = Math.Round(driftScore, 1),
                DriftLabel = driftLabel,
                GeneratedAtUtc = DateTimeOffset.UtcNow
            });
        })
        .WithName("IdentitySubjectBaseline")
        .WithSummary("Get the 7-day behavioral baseline and drift score for a subject.")
        .Produces<SubjectBaselineDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        return app;
    }
}
