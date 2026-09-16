using Microsoft.Extensions.Logging;
using PoWatch.Application.Contracts;
using PoWatch.Domain.Models;
using PoWatch.Shared.Models;

namespace PoWatch.Application.Services;

/// <summary>
/// Creates, validates, and revokes family share links. The link is a single purpose URL that
/// exposes an anonymised snapshot — there is no second factor, no per-request authorisation.
/// The security model is the URL itself: long, unguessable, expires fast, revocable, and
/// carries no raw PII in the payload (subjects are humanised before the snapshot is taken).
/// </summary>
public sealed class ShareLinkService(
    IShareLinkRepository repository,
    ArchivesService archivesService,
    ILogger<ShareLinkService> logger)
{
    /// <summary>Create a new share link for the given date. The snapshot is rendered at creation
    /// time so the link remains useful even after the underlying chapter changes.</summary>
    public async Task<ShareLink> CreateAsync(
        DateOnly date,
        string? createdByUserId,
        TimeSpan? ttl,
        CancellationToken cancellationToken)
    {
        var effectiveTtl = ttl ?? TimeSpan.FromHours(ShareLink.DefaultTtlHours);
        if (effectiveTtl <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Share link TTL must be positive.");
        }

        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid().ToString("N");

        // Snapshot the anonymised prose at creation. Building the narrative server-side, here,
        // means the share path doesn't depend on the live Archives pipeline staying healthy —
        // a viewer opening the link sees what the caregiver saw when they clicked Share, not
        // a possibly-altered copy of today.
        var chapter = await archivesService.GetChapterAsync(date, NarrativeMode.Prose, cancellationToken);
        var snapshot = chapter.ClinicalNarrative;

        var link = new ShareLink
        {
            Id = id,
            Date = date,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(effectiveTtl),
            CreatedByUserId = createdByUserId,
            AnonymisedNarrative = snapshot
        };

        var stored = await repository.AddAsync(link, cancellationToken);

        logger.LogInformation(
            "Share link created. LinkId={LinkId} Date={Date} TtlHours={TtlHours} Actor={Actor}",
            stored.Id, stored.Date, effectiveTtl.TotalHours, createdByUserId ?? "anonymous");

        return stored;
    }

    public Task<ShareLink?> GetAsync(string id, CancellationToken cancellationToken) =>
        repository.GetAsync(id, cancellationToken);

    public async Task<bool> RevokeAsync(string id, string? actorUserId, CancellationToken cancellationToken)
    {
        var link = await repository.GetAsync(id, cancellationToken);
        if (link is null) return false;

        if (link.RevokedAtUtc is null)
        {
            link.RevokedAtUtc = DateTimeOffset.UtcNow;
            await repository.UpdateAsync(link, cancellationToken);
        }

        logger.LogInformation(
            "Share link revoked. LinkId={LinkId} Actor={Actor}",
            id, actorUserId ?? "anonymous");
        return true;
    }
}
