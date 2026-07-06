using System.Collections.Concurrent;
using PoWatch.Application.Contracts;
using PoWatch.Domain.Models;
using PoWatch.Domain.Services;

namespace PoWatch.Infrastructure.Persistence;

/// <summary>
/// Thread-safe in-memory subject repository using immutable update patterns.
/// All mutations create new SubjectProfile instances to avoid shared mutable state issues.
/// </summary>
public sealed class InMemorySubjectRepository : ISubjectRepository
{
    private readonly ConcurrentDictionary<string, SubjectProfile> _subjects = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SubjectProfile> _byDisplayName = new(StringComparer.OrdinalIgnoreCase);
    private int _sequence = 0;

    public Task<IReadOnlyList<SubjectProfile>> GetAllAsync(CancellationToken cancellationToken)
    {
        var items = _subjects.Values
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Task.FromResult<IReadOnlyList<SubjectProfile>>(items);
    }

    public Task<SubjectProfile> GetOrCreateAsync(string? hint, CancellationToken cancellationToken)
    {
        var normalized = (hint ?? string.Empty).Trim();
        var now = DateTimeOffset.UtcNow;

        if (!string.IsNullOrWhiteSpace(normalized))
        {
            // Try to find existing by ID first
            if (_subjects.TryGetValue(normalized, out var existingById))
            {
                // Return updated copy with new LastSeenUtc
                var updated = UpdateSubjectTimestamps(existingById, now);
                return Task.FromResult<SubjectProfile>(updated);
            }

            // Try to find by display name
            if (_byDisplayName.TryGetValue(normalized, out var existingByName))
            {
                var updated = UpdateSubjectTimestamps(existingByName, now);
                return Task.FromResult<SubjectProfile>(updated);
            }

            // Create new subject
            var known = CreateSubjectProfile(normalized, normalized, now, now, knownIdentity: !normalized.StartsWith("Subject-", StringComparison.OrdinalIgnoreCase));
            _subjects[known.SubjectId] = known;
            _byDisplayName[known.DisplayName] = known;
            SubjectIdSlugger.RegisterSlug(known.SubjectId, known.SubjectId);
            return Task.FromResult<SubjectProfile>(known);
        }

        var subjectId = $"Subject-{Interlocked.Increment(ref _sequence)}";
        var generated = CreateSubjectProfile(subjectId, subjectId, now, now, knownIdentity: false);
        _subjects[subjectId] = generated;
        _byDisplayName[generated.DisplayName] = generated;
        SubjectIdSlugger.RegisterSlug(subjectId, subjectId);
        return Task.FromResult<SubjectProfile>(generated);
    }

    public Task<SubjectProfile?> GetByIdAsync(string subjectId, CancellationToken cancellationToken)
    {
        _subjects.TryGetValue(subjectId, out var subject);
        return Task.FromResult(subject);
    }

    public Task<SubjectProfile> RenameAsync(string subjectId, string newDisplayName, CancellationToken cancellationToken)
    {
        if (!_subjects.TryGetValue(subjectId, out var subject))
        {
            throw new InvalidOperationException($"Subject '{subjectId}' was not found.");
        }

        var trimmed = newDisplayName.Trim();
        var existingSlug = subject.SubjectId;
        var canonicalId = ResolveCanonicalSubjectId(subject.SubjectId, trimmed, subjectId);

        // Create new immutable subject profile
        var renamed = CreateSubjectProfile(
            canonicalId,
            trimmed,
            subject.FirstSeenUtc,
            DateTimeOffset.UtcNow,
            knownIdentity: true,
            lastActivity: subject.LastActivity,
            lastActivityIsOutlier: subject.LastActivityIsOutlier);

        // Atomic update: remove old, add new
        if (!_subjects.TryRemove(subjectId, out _))
        {
            throw new InvalidOperationException($"Failed to remove subject '{subjectId}' during rename.");
        }

        // Remove old display name mapping
        _byDisplayName.TryRemove(subject.DisplayName, out _);

        // Unregister old slug if ID changed
        if (!string.Equals(subjectId, canonicalId, StringComparison.OrdinalIgnoreCase))
        {
            SubjectIdSlugger.UnregisterSlug(subjectId, existingSlug);
        }

        // Add new
        _subjects[renamed.SubjectId] = renamed;
        _byDisplayName[renamed.DisplayName] = renamed;
        SubjectIdSlugger.RegisterSlug(renamed.SubjectId, renamed.SubjectId);

        return Task.FromResult(renamed);
    }

    public Task<SubjectProfile> MergeAsync(string primarySubjectId, string secondarySubjectId, string? explicitName, CancellationToken cancellationToken)
    {
        _subjects.TryGetValue(primarySubjectId, out var primary);
        _subjects.TryGetValue(secondarySubjectId, out var secondary);

        // Create primary if it doesn't exist
        if (primary is null)
        {
            primary = CreateSubjectProfile(
                primarySubjectId,
                primarySubjectId,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                knownIdentity: !primarySubjectId.StartsWith("Subject-", StringComparison.OrdinalIgnoreCase));
        }

        var displayName = string.IsNullOrWhiteSpace(explicitName) ? primary.DisplayName : explicitName.Trim();
        var existingSlug = primary.SubjectId;
        var canonicalId = ResolveCanonicalSubjectId(primary.SubjectId, displayName, primarySubjectId, secondarySubjectId);

        // Determine earliest firstSeen and latest lastSeen
        var firstSeen = MinDate(
            primary.FirstSeenUtc,
            secondary?.FirstSeenUtc ?? DateTimeOffset.MaxValue);
        var lastSeen = MaxDate(
            primary.LastSeenUtc,
            secondary?.LastSeenUtc ?? DateTimeOffset.MinValue);

        // Create merged profile
        var merged = CreateSubjectProfile(
            canonicalId,
            displayName,
            firstSeen,
            lastSeen,
            knownIdentity: true,
            lastActivity: primary.LastActivity,
            lastActivityIsOutlier: primary.LastActivityIsOutlier);

        // Atomic merge: remove both, add merged
        _subjects.TryRemove(primarySubjectId, out _);
        _subjects.TryRemove(secondarySubjectId, out _);
        _byDisplayName.TryRemove(primary.DisplayName, out _);
        if (secondary is not null)
        {
            _byDisplayName.TryRemove(secondary.DisplayName, out _);
        }

        // Unregister old slugs
        SubjectIdSlugger.UnregisterSlug(primarySubjectId, primary.SubjectId);
        if (secondary is not null)
        {
            SubjectIdSlugger.UnregisterSlug(secondarySubjectId, secondary.SubjectId);
        }

        // Add merged
        _subjects[merged.SubjectId] = merged;
        _byDisplayName[merged.DisplayName] = merged;
        SubjectIdSlugger.RegisterSlug(merged.SubjectId, merged.SubjectId);

        return Task.FromResult(merged);
    }

    public Task<SubjectProfile> RegisterKnownAsync(string displayName, CancellationToken cancellationToken)
    {
        var trimmed = displayName.Trim();
        var subjectId = ResolveCanonicalSubjectId(string.Empty, trimmed);
        var now = DateTimeOffset.UtcNow;

        if (_subjects.TryGetValue(subjectId, out var existing))
            return Task.FromResult(existing);

        var profile = CreateSubjectProfile(subjectId, trimmed, now, now, knownIdentity: true);
        _subjects[subjectId] = profile;
        _byDisplayName[trimmed] = profile;
        SubjectIdSlugger.RegisterSlug(subjectId, subjectId);
        return Task.FromResult(profile);
    }

    public Task UpdateLastActivityAsync(string subjectId, string activity, bool isOutlier, CancellationToken cancellationToken)
    {
        if (_subjects.TryGetValue(subjectId, out var subject))
        {
            // Create immutable copy with updated activity
            var updated = CreateSubjectProfile(
                subject.SubjectId,
                subject.DisplayName,
                subject.FirstSeenUtc,
                DateTimeOffset.UtcNow,
                subject.IsKnownIdentity,
                lastActivity: activity,
                lastActivityIsOutlier: isOutlier);

            // Atomic update
            _subjects[subjectId] = updated;
            _byDisplayName[subject.DisplayName] = updated;
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(string subjectId, CancellationToken cancellationToken)
    {
        // Idempotent: Rename/Merge already swap entries atomically in-process, so by the time the
        // service calls this the row is usually already gone — removing again is a harmless no-op.
        if (_subjects.TryRemove(subjectId, out var removed))
        {
            _byDisplayName.TryRemove(removed.DisplayName, out _);
        }

        return Task.CompletedTask;
    }

    private static SubjectProfile UpdateSubjectTimestamps(SubjectProfile subject, DateTimeOffset now)
    {
        if (subject.LastSeenUtc >= now.AddSeconds(-1))
        {
            // Already recently updated
            return subject;
        }

        return CreateSubjectProfile(
            subject.SubjectId,
            subject.DisplayName,
            subject.FirstSeenUtc,
            now,
            subject.IsKnownIdentity,
            subject.LastActivity,
            subject.LastActivityIsOutlier);
    }

    private static SubjectProfile CreateSubjectProfile(
        string subjectId,
        string displayName,
        DateTimeOffset firstSeenUtc,
        DateTimeOffset lastSeenUtc,
        bool knownIdentity = false,
        string? lastActivity = null,
        bool lastActivityIsOutlier = false)
    {
        return new SubjectProfile
        {
            SubjectId = subjectId,
            DisplayName = displayName,
            IsKnownIdentity = knownIdentity,
            FirstSeenUtc = firstSeenUtc,
            LastSeenUtc = lastSeenUtc,
            LastActivity = lastActivity,
            LastActivityIsOutlier = lastActivityIsOutlier
        };
    }

    private static DateTimeOffset MinDate(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
    private static DateTimeOffset MaxDate(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;

    private string ResolveCanonicalSubjectId(string currentSubjectId, string displayName, params string[] allowedSubjectIds)
    {
        if (!string.IsNullOrWhiteSpace(currentSubjectId)
            && !currentSubjectId.StartsWith("Subject-", StringComparison.OrdinalIgnoreCase))
        {
            return currentSubjectId;
        }

        var baseId = SubjectIdSlugger.BuildCanonicalSubjectId(displayName);
        var candidate = baseId;
        var suffix = 2;

        while (true)
        {
            if (allowedSubjectIds.Any(id => string.Equals(id, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }

            if (!_subjects.ContainsKey(candidate))
            {
                return candidate;
            }

            candidate = $"{baseId}-{suffix}";
            suffix++;
        }
    }
}
