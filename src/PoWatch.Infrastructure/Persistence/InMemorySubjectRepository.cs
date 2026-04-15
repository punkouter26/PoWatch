using System.Collections.Concurrent;
using System.Text;
using PoWatch.Application.Contracts;
using PoWatch.Domain.Models;

namespace PoWatch.Infrastructure.Persistence;

public sealed class InMemorySubjectRepository : ISubjectRepository
{
    private readonly ConcurrentDictionary<string, SubjectProfile> _subjects = new(StringComparer.OrdinalIgnoreCase);
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
            var existingById = _subjects.TryGetValue(normalized, out var existingSubject)
                ? existingSubject
                : _subjects.Values.FirstOrDefault(x =>
                    string.Equals(x.DisplayName, normalized, StringComparison.OrdinalIgnoreCase));

            if (existingById is not null)
            {
                existingById.LastSeenUtc = now;
                return Task.FromResult(existingById);
            }

            var known = new SubjectProfile
            {
                SubjectId = normalized,
                DisplayName = normalized,
                IsKnownIdentity = !normalized.StartsWith("Subject-", StringComparison.OrdinalIgnoreCase),
                FirstSeenUtc = now,
                LastSeenUtc = now
            };

            _subjects[known.SubjectId] = known;
            return Task.FromResult(known);
        }

        var subjectId = $"Subject-{Interlocked.Increment(ref _sequence)}";
        var generated = new SubjectProfile
        {
            SubjectId = subjectId,
            DisplayName = subjectId,
            IsKnownIdentity = false,
            FirstSeenUtc = now,
            LastSeenUtc = now
        };

        _subjects[subjectId] = generated;
        return Task.FromResult(generated);
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
        var canonicalId = ResolveCanonicalSubjectId(subjectId, trimmed);

        var renamed = new SubjectProfile
        {
            SubjectId = canonicalId,
            DisplayName = trimmed,
            IsKnownIdentity = true,
            FirstSeenUtc = subject.FirstSeenUtc,
            LastSeenUtc = subject.LastSeenUtc
        };

        _subjects.TryRemove(subjectId, out _);
        _subjects[renamed.SubjectId] = renamed;

        return Task.FromResult(renamed);
    }

    public Task<SubjectProfile> MergeAsync(string primarySubjectId, string secondarySubjectId, string? explicitName, CancellationToken cancellationToken)
    {
        if (!_subjects.TryGetValue(primarySubjectId, out var primary))
        {
            primary = new SubjectProfile
            {
                SubjectId = primarySubjectId,
                DisplayName = primarySubjectId,
                IsKnownIdentity = !primarySubjectId.StartsWith("Subject-", StringComparison.OrdinalIgnoreCase),
                FirstSeenUtc = DateTimeOffset.UtcNow,
                LastSeenUtc = DateTimeOffset.UtcNow
            };
        }

        _subjects.TryGetValue(secondarySubjectId, out var secondary);

        var displayName = string.IsNullOrWhiteSpace(explicitName) ? primary.DisplayName : explicitName.Trim();
        var canonicalId = ResolveCanonicalSubjectId(primary.SubjectId, displayName);

        var merged = new SubjectProfile
        {
            SubjectId = canonicalId,
            DisplayName = displayName,
            IsKnownIdentity = true,
            FirstSeenUtc = secondary is null
                ? primary.FirstSeenUtc
                : new[] { primary.FirstSeenUtc, secondary.FirstSeenUtc }.Min(),
            LastSeenUtc = secondary is null
                ? primary.LastSeenUtc
                : new[] { primary.LastSeenUtc, secondary.LastSeenUtc }.Max()
        };

        _subjects.TryRemove(primarySubjectId, out _);
        _subjects.TryRemove(secondarySubjectId, out _);
        _subjects[merged.SubjectId] = merged;

        return Task.FromResult(merged);
    }

    private static string ResolveCanonicalSubjectId(string currentSubjectId, string displayName)
    {
        if (!currentSubjectId.StartsWith("Subject-", StringComparison.OrdinalIgnoreCase)
            || string.Equals(currentSubjectId, displayName, StringComparison.OrdinalIgnoreCase))
        {
            return currentSubjectId;
        }

        return BuildCanonicalSubjectId(displayName);
    }

    private static string BuildCanonicalSubjectId(string displayName)
    {
        var builder = new StringBuilder();

        foreach (var character in displayName.Trim().ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '-');
        }

        return builder.ToString().Trim('-');
    }
}
