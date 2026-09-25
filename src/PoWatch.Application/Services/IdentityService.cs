using Microsoft.Extensions.Logging;
using PoWatch.Application.Contracts;
using PoWatch.Application.Mappers;
using PoWatch.Domain.Models;
using PoWatch.Shared.Models;

namespace PoWatch.Application.Services;

public sealed class IdentityService(
    ISubjectRepository subjectRepository,
    IObservationRepository observationRepository,
    ISubjectRevisionEventRepository revisionEventRepository,
    ILogger<IdentityService> logger)
{
    public async Task<IReadOnlyList<SubjectProfileDto>> GetSubjectsAsync(CancellationToken cancellationToken)
    {
        var profiles = await subjectRepository.GetAllAsync(cancellationToken);
        return profiles.Select(p => new SubjectProfileDto
        {
            SubjectId = p.SubjectId,
            DisplayName = p.DisplayName,
            IsKnownIdentity = p.IdentityStatus == IdentityStatus.Known,
            FirstSeenUtc = p.FirstSeenUtc,
            LastSeenUtc = p.LastSeenUtc
        }).ToList();
    }

    public async Task<IdentityRevisionResultDto> RenameAsync(string subjectId, RenameSubjectRequestDto request, string? actorUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(subjectId))
        {
            throw new InvalidOperationException("SubjectId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.NewName))
        {
            throw new InvalidOperationException("NewName is required.");
        }

        logger.LogInformation(
            "Inline rename requested. SubjectId={SubjectId}, NewName={NewName}",
            subjectId,
            request.NewName);

        // Order matters: upsert the canonical profile, rewrite the observation history to it, and only
        // THEN delete the old profile row. If any step faults, observations are never left orphaned.
        var canonical = await subjectRepository.RenameAsync(subjectId, request.NewName, cancellationToken);
        var subjectIdChanged = !string.Equals(subjectId, canonical.SubjectId, StringComparison.OrdinalIgnoreCase);
        var rewritten = await observationRepository.MergeSubjectAsync(subjectId, canonical, cancellationToken);
        if (subjectIdChanged)
        {
            await subjectRepository.DeleteAsync(subjectId, cancellationToken);
        }
        var removed = subjectIdChanged ? 1 : 0;

        // Audit trail: record what just happened so the caregiver can later see "this id was renamed
        // from X to Y on Z by W". The merge here writes against the canonical id so the history
        // stays under the surviving subject, not the deleted one.
        await revisionEventRepository.AppendAsync(new SubjectRevisionEvent
        {
            SubjectId = canonical.SubjectId,
            OccurredAtUtc = DateTimeOffset.UtcNow,
            Kind = Domain.Models.SubjectRevisionKind.Renamed,
            Detail = subjectIdChanged
                ? $"Renamed from '{subjectId}' to '{canonical.DisplayName}'."
                : $"Display name changed to '{canonical.DisplayName}'.",
            ActorUserId = actorUserId
        }, cancellationToken);
        if (subjectIdChanged)
        {
            // The absorbed id is no longer current but the row gets a final Deleted entry so a future
            // history fetch against this id still has its terminus. We do not synthesize a MergedInto
            // here — that event is reserved for MergeAsync where two named identities collapse.
            await revisionEventRepository.AppendAsync(new SubjectRevisionEvent
            {
                SubjectId = SubjectId.From(subjectId),
                OccurredAtUtc = DateTimeOffset.UtcNow,
                Kind = Domain.Models.SubjectRevisionKind.Deleted,
                Detail = $"Replaced by canonical id '{canonical.SubjectId}'.",
                ActorUserId = actorUserId
            }, cancellationToken);
        }

        logger.LogInformation(
            "Inline rename completed. CanonicalSubjectId={CanonicalSubjectId}, CanonicalName={CanonicalName}, EventsRewritten={EventsRewritten}, SubjectsRemoved={SubjectsRemoved}",
            canonical.SubjectId,
            canonical.DisplayName,
            rewritten,
            removed);

        return new IdentityRevisionResultDto
        {
            CanonicalSubjectId = canonical.SubjectId,
            CanonicalName = canonical.DisplayName,
            EventsRewritten = rewritten,
            SubjectsRemoved = removed
        };
    }

    public async Task<IdentityRevisionResultDto> MergeAsync(MergeIdentityRequestDto request, string? actorUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PrimarySubjectId) || string.IsNullOrWhiteSpace(request.SecondarySubjectId))
        {
            throw new InvalidOperationException("PrimarySubjectId and SecondarySubjectId are required.");
        }

        if (string.Equals(request.PrimarySubjectId, request.SecondarySubjectId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Primary and secondary subjects must be different.");
        }

        logger.LogInformation(
            "Merge requested. PrimarySubjectId={PrimarySubjectId}, SecondarySubjectId={SecondarySubjectId}, NewDisplayName={NewDisplayName}",
            request.PrimarySubjectId,
            request.SecondarySubjectId,
            request.NewDisplayName);

        // Rewrite-then-delete ordering: MergeAsync upserts the canonical profile but no longer deletes
        // the source rows. We rewrite both subjects' observation history to the canonical id first, then
        // delete the now-empty source profiles — so an interrupted merge never orphans history.
        var merged = await subjectRepository.MergeAsync(
            request.PrimarySubjectId,
            request.SecondarySubjectId,
            request.NewDisplayName,
            cancellationToken);

        var rewritten = 0;
        // A source row is deleted only if it is NOT the surviving canonical id. Guarding BOTH sides means
        // that whichever id MergeAsync canonicalizes to (primary OR secondary) is never itself deleted — no
        // path can drop the profile that history was just rewritten onto.
        var primaryChanged = !string.Equals(request.PrimarySubjectId, merged.SubjectId, StringComparison.OrdinalIgnoreCase);
        var secondaryChanged = !string.Equals(request.SecondarySubjectId, merged.SubjectId, StringComparison.OrdinalIgnoreCase);

        if (primaryChanged)
        {
            rewritten += await observationRepository.MergeSubjectAsync(request.PrimarySubjectId, merged, cancellationToken);
        }

        rewritten += await observationRepository.MergeSubjectAsync(request.SecondarySubjectId, merged, cancellationToken);

        // History is now safely under the canonical id — delete the (non-canonical) source profile rows.
        if (primaryChanged)
        {
            await subjectRepository.DeleteAsync(request.PrimarySubjectId, cancellationToken);
        }
        if (secondaryChanged)
        {
            await subjectRepository.DeleteAsync(request.SecondarySubjectId, cancellationToken);
        }

        var removed = (primaryChanged ? 1 : 0) + (secondaryChanged ? 1 : 0);

        // Audit trail. The canonical id gets a MergedFrom row so its history shows the merge happened
        // and from which id. Each absorbed id gets a MergedInto row pinned to the canonical id, plus a
        // Deleted row to mark the terminus of its standalone profile.
        var occurredAt = DateTimeOffset.UtcNow;
        await revisionEventRepository.AppendAsync(new SubjectRevisionEvent
        {
            SubjectId = merged.SubjectId,
            OccurredAtUtc = occurredAt,
            Kind = Domain.Models.SubjectRevisionKind.MergedFrom,
            Detail = $"Merged from {DescribeSourcePair(request.PrimarySubjectId, request.SecondarySubjectId, merged.SubjectId)}.",
            ActorUserId = actorUserId
        }, cancellationToken);

        if (primaryChanged)
        {
            await revisionEventRepository.AppendAsync(new SubjectRevisionEvent
            {
                SubjectId = SubjectId.From(request.PrimarySubjectId),
                OccurredAtUtc = occurredAt,
                Kind = Domain.Models.SubjectRevisionKind.MergedInto,
                Detail = $"Merged into '{merged.SubjectId}' ({merged.DisplayName}).",
                ActorUserId = actorUserId
            }, cancellationToken);
            await revisionEventRepository.AppendAsync(new SubjectRevisionEvent
            {
                SubjectId = SubjectId.From(request.PrimarySubjectId),
                OccurredAtUtc = occurredAt,
                Kind = Domain.Models.SubjectRevisionKind.Deleted,
                Detail = $"Replaced by canonical id '{merged.SubjectId}'.",
                ActorUserId = actorUserId
            }, cancellationToken);
        }

        if (secondaryChanged)
        {
            await revisionEventRepository.AppendAsync(new SubjectRevisionEvent
            {
                SubjectId = SubjectId.From(request.SecondarySubjectId),
                OccurredAtUtc = occurredAt,
                Kind = Domain.Models.SubjectRevisionKind.MergedInto,
                Detail = $"Merged into '{merged.SubjectId}' ({merged.DisplayName}).",
                ActorUserId = actorUserId
            }, cancellationToken);
            await revisionEventRepository.AppendAsync(new SubjectRevisionEvent
            {
                SubjectId = SubjectId.From(request.SecondarySubjectId),
                OccurredAtUtc = occurredAt,
                Kind = Domain.Models.SubjectRevisionKind.Deleted,
                Detail = $"Replaced by canonical id '{merged.SubjectId}'.",
                ActorUserId = actorUserId
            }, cancellationToken);
        }

        logger.LogInformation(
            "Merge completed. CanonicalSubjectId={CanonicalSubjectId}, CanonicalName={CanonicalName}, EventsRewritten={EventsRewritten}, SubjectsRemoved={SubjectsRemoved}",
            merged.SubjectId,
            merged.DisplayName,
            rewritten,
            removed);

        return new IdentityRevisionResultDto
        {
            CanonicalSubjectId = merged.SubjectId,
            CanonicalName = merged.DisplayName,
            EventsRewritten = rewritten,
            SubjectsRemoved = removed
        };
    }

    private static string DescribeSourcePair(string primary, string secondary, string canonical) =>
        string.Equals(primary, canonical, StringComparison.OrdinalIgnoreCase)
            ? $"secondary '{secondary}'"
            : $"primary '{primary}'";

    /// <summary>Pre-registers a known subject without needing an observation.</summary>
    public async Task<SubjectProfileDto> RegisterKnownSubjectAsync(RegisterSubjectRequestDto request, string? actorUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName))
            throw new InvalidOperationException("DisplayName is required.");

        logger.LogInformation("RegisterKnownSubject requested. DisplayName={DisplayName}", request.DisplayName);

        var profile = await subjectRepository.RegisterKnownAsync(request.DisplayName, cancellationToken);

        await revisionEventRepository.AppendAsync(new SubjectRevisionEvent
        {
            SubjectId = profile.SubjectId,
            OccurredAtUtc = DateTimeOffset.UtcNow,
            Kind = Domain.Models.SubjectRevisionKind.Created,
            Detail = $"Pre-registered as known identity '{profile.DisplayName}'.",
            ActorUserId = actorUserId
        }, cancellationToken);

        logger.LogInformation(
            "RegisterKnownSubject completed. SubjectId={SubjectId} DisplayName={DisplayName}",
            profile.SubjectId,
            profile.DisplayName);

        return new SubjectProfileDto
        {
            SubjectId = profile.SubjectId,
            DisplayName = profile.DisplayName,
            IsKnownIdentity = profile.IdentityStatus == IdentityStatus.Known,
            FirstSeenUtc = profile.FirstSeenUtc,
            LastSeenUtc = profile.LastSeenUtc
        };
    }

    /// <summary>Returns a subject's full revision history, most-recent first. Empty list is never
    /// returned — a brand-new subject has at least its <see cref="SubjectRevisionKind.Created"/>
    /// row from <see cref="RegisterKnownSubjectAsync"/>, and an implicitly-created subject gets its
    /// Created row written by <see cref="ISubjectRepository.GetOrCreateAsync"/> via the repository
    /// adapter.</summary>
    public async Task<IReadOnlyList<SubjectRevisionEventDto>> GetHistoryAsync(string subjectId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(subjectId))
            throw new InvalidOperationException("SubjectId is required.");

        var events = await revisionEventRepository.GetHistoryAsync(subjectId, cancellationToken);
        return events.ToDtos();
    }

    /// <summary>
    /// Returns a live status snapshot for every known subject, including their last 10 events
    /// and count of notable events from today.
    /// </summary>
    public async Task<IReadOnlyList<SubjectLiveStatusDto>> GetLiveDashboardStatusAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("Live dashboard status requested.");

        var profiles = await subjectRepository.GetAllAsync(cancellationToken);
        // The caregiver's LOCAL calendar day via ShiftClock — not the UTC partition key. Reading
        // today's UTC partition shifted "notable today" by the UTC offset, dropping the
        // local evening and pulling in the small hours that belong to yesterday.
        var today = ShiftClock.Today();
        var todayEvents = await ShiftClock.LoadLocalDayAsync(observationRepository, today, cancellationToken);

        var result = new List<SubjectLiveStatusDto>(profiles.Count);

        foreach (var profile in profiles)
        {
            var subjectEvents = todayEvents
                .Where(e => string.Equals(e.SubjectId, profile.SubjectId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(e => e.ObservedAtUtc)
                .ToList();

            var recentEvents = subjectEvents
                .Take(10)
                .ToDtos();

            result.Add(new SubjectLiveStatusDto
            {
                SubjectId = profile.SubjectId,
                DisplayName = profile.DisplayName,
                IsKnownIdentity = profile.IdentityStatus == IdentityStatus.Known,
                LastSeenUtc = profile.LastSeenUtc,
                LastActivity = profile.LastActivity ?? string.Empty,
                LastActivityIsOutlier = profile.LastActivityIsOutlier,
                NotableTodayCount = subjectEvents.Count(e => e.IsSignificant),
                RecentEvents = recentEvents
            });
        }

        logger.LogDebug("Live dashboard status built. SubjectCount={SubjectCount}", result.Count);
        return result;
    }
}
