using System.Diagnostics;
using PoWatch.Application.Services;
using PoWatch.Domain.Models;
using PoWatch.Shared.Models;

namespace PoWatch.Api.Features.Share;

internal static class ShareLinkEndpoints
{
    internal static IEndpointRouteBuilder MapShareLinkFeature(this IEndpointRouteBuilder app)
    {
        // Two endpoint groups: one for the caregiver (creating + revoking), one for the family
        // member (reading the snapshot). The read path is anonymous — that is the entire
        // product model — so it opts out of the default-deny authorization policy via
        // AllowAnonymous. The id is the only secret; the URL is the credential.
        var caregiver = app.MapGroup("/api/share/links").WithTags("Share Links").RequireAuthorization();

        caregiver.MapPost("", async (
            CreateShareLinkRequestDto? request,
            HttpContext httpContext,
            ShareLinkService service,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            request ??= new CreateShareLinkRequestDto();
            var date = request.Date ?? DateOnly.FromDateTime(DateTime.Now);
            var ttl = request.TtlHours is { } hours && hours > 0
                ? TimeSpan.FromHours(Math.Min(hours, 168)) // hard cap at 7 days; never expose an unbounded TTL
                : TimeSpan.FromHours(ShareLink.DefaultTtlHours);

            var actor = ResolveActor(httpContext);
            var link = await service.CreateAsync(date, actor, ttl, cancellationToken);

            logger.LogInformation(
                "Family share link created. LinkId={LinkId} Date={Date} TtlHours={TtlHours} Actor={Actor}",
                link.Id, link.Date, ttl.TotalHours, actor);

            return Results.Ok(ToSummary(link));
        })
        .WithName("ShareLinkCreate")
        .WithSummary("Create a single-purpose family share link for a day. TTL is capped at 7 days.")
        .Produces<ShareLinkSummaryDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        caregiver.MapDelete("{id}", async (
            string id,
            HttpContext httpContext,
            ShareLinkService service,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            var actor = ResolveActor(httpContext);
            var revoked = await service.RevokeAsync(id, actor, cancellationToken);
            if (!revoked) return Results.NotFound();
            logger.LogInformation("Family share link revoked via API. LinkId={LinkId} Actor={Actor}", id, actor);
            return Results.NoContent();
        })
        .WithName("ShareLinkRevoke")
        .WithSummary("Revoke a family share link. Idempotent — second revoke returns 404.")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound);

        // Anonymous read path. Living at /api/share/view/{id} keeps it discoverable only to those
        // who already hold the URL.
        var family = app.MapGroup("/api/share/view").WithTags("Share Links (anonymous)").AllowAnonymous();

        family.MapGet("{id}", async (
            string id,
            ShareLinkService service,
            CancellationToken cancellationToken) =>
        {
            var link = await service.GetAsync(id, cancellationToken);
            if (link is null) return Results.NotFound();
            if (!link.IsUsable)
            {
                // 410 Gone is the right shape — the resource existed and is now permanently
                // unavailable, distinct from a never-existed id (404). Family members who paste
                // the URL into a chat a day later get a clean answer instead of a stale page.
                return Results.StatusCode(StatusCodes.Status410Gone);
            }

            return Results.Ok(new ShareLinkViewDto
            {
                Date = link.Date,
                ExpiresAtUtc = link.ExpiresAtUtc,
                AnonymisedNarrative = link.AnonymisedNarrative
            });
        })
        .WithName("ShareLinkView")
        .WithSummary("Family view of a shared day. Anonymous. Returns 410 Gone when expired or revoked.")
        .Produces<ShareLinkViewDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status410Gone);

        return app;
    }

    private static ShareLinkSummaryDto ToSummary(Domain.Models.ShareLink link) => new()
    {
        Id = link.Id,
        Date = link.Date,
        CreatedAtUtc = link.CreatedAtUtc,
        ExpiresAtUtc = link.ExpiresAtUtc,
        RevokedAtUtc = link.RevokedAtUtc
    };

    private static string ResolveActor(HttpContext httpContext) =>
        httpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? httpContext.User?.FindFirst("sub")?.Value
        ?? "anonymous";
}
