using System.Diagnostics;
using PoWatch.Application.Services;
using PoWatch.Shared.Models;

namespace PoWatch.Api.Endpoints;

internal static class IdentityEndpoints
{
    internal static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/identity").WithTags("Identity");

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

        return app;
    }
}
