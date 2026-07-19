using Microsoft.Extensions.Logging;
using PoWatch.Application.Contracts;
using PoWatch.Domain.Models;
using PoWatch.Shared.Models;

namespace PoWatch.Application.Services;

public sealed class IdentityService(
    ISubjectRepository subjectRepository,
    IObservationRepository observationRepository,
    IAcknowledgementRegistry acknowledgementRegistry,
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

    public async Task<IdentityRevisionResultDto> RenameAsync(string subjectId, RenameSubjectRequestDto request, CancellationToken cancellationToken)
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

    public async Task<IdentityRevisionResultDto> MergeAsync(MergeIdentityRequestDto request, CancellationToken cancellationToken)
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

    /// <summary>Pre-registers a known subject without needing an observation.</summary>
    public async Task<SubjectProfileDto> RegisterKnownSubjectAsync(RegisterSubjectRequestDto request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName))
            throw new InvalidOperationException("DisplayName is required.");

        logger.LogInformation("RegisterKnownSubject requested. DisplayName={DisplayName}", request.DisplayName);

        var profile = await subjectRepository.RegisterKnownAsync(request.DisplayName, cancellationToken);

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

    /// <summary>
    /// Returns a live status snapshot for every known subject, including their last 10 events
    /// and count of unacknowledged significant events from today.
    /// </summary>
    public async Task<IReadOnlyList<SubjectLiveStatusDto>> GetLiveDashboardStatusAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("Live dashboard status requested.");

        var profiles = await subjectRepository.GetAllAsync(cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var todayEvents = await observationRepository.GetByDateAsync(today, cancellationToken);

        var result = new List<SubjectLiveStatusDto>(profiles.Count);

        foreach (var profile in profiles)
        {
            var subjectEvents = todayEvents
                .Where(e => string.Equals(e.SubjectId, profile.SubjectId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(e => e.ObservedAtUtc)
                .ToList();

            var recentEvents = subjectEvents
                .Take(10)
                .Select(e => new ObservationEventDto
                {
                    Id = e.Id,
                    ObservedAtUtc = e.ObservedAtUtc,
                    SubjectId = e.SubjectId,
                    SubjectDisplayName = e.SubjectDisplayName,
                    Activity = e.Activity,
                    ClinicalDescription = e.ClinicalDescription,
                    IsSignificant = e.IsSignificant,
                    SignificantReason = e.SignificantReason,
                    IsClinicalOutlier = e.IsClinicalOutlier,
                    ImageReference = e.ImageReference
                })
                .ToList();

            result.Add(new SubjectLiveStatusDto
            {
                SubjectId = profile.SubjectId,
                DisplayName = profile.DisplayName,
                IsKnownIdentity = profile.IdentityStatus == IdentityStatus.Known,
                LastSeenUtc = profile.LastSeenUtc,
                LastActivity = profile.LastActivity ?? string.Empty,
                LastActivityIsOutlier = profile.LastActivityIsOutlier,
                UnacknowledgedSignificantCount = subjectEvents.Count(e => e.IsSignificant && !acknowledgementRegistry.IsAcknowledged(e.Id)),
                RecentEvents = recentEvents
            });
        }

        logger.LogDebug("Live dashboard status built. SubjectCount={SubjectCount}", result.Count);
        return result;
    }
}
