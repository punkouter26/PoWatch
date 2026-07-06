using PoWatch.Domain.Models;

namespace PoWatch.Application.Contracts;

public interface ISubjectRepository
{
    Task<IReadOnlyList<SubjectProfile>> GetAllAsync(CancellationToken cancellationToken);

    Task<SubjectProfile> GetOrCreateAsync(string? hint, CancellationToken cancellationToken);

    Task<SubjectProfile?> GetByIdAsync(string subjectId, CancellationToken cancellationToken);

    Task<SubjectProfile> RenameAsync(string subjectId, string newDisplayName, CancellationToken cancellationToken);

    Task<SubjectProfile> MergeAsync(string primarySubjectId, string secondarySubjectId, string? explicitName, CancellationToken cancellationToken);

    /// <summary>Pre-registers a known subject with a given display name without requiring an observation.</summary>
    Task<SubjectProfile> RegisterKnownAsync(string displayName, CancellationToken cancellationToken);

    /// <summary>Updates the cached last-observed activity on the subject row (O(1) subject-keyed write).</summary>
    Task UpdateLastActivityAsync(string subjectId, string activity, bool isOutlier, CancellationToken cancellationToken);

    /// <summary>
    /// Idempotently removes a subject profile row. Callers rewrite that subject's observations to the
    /// canonical id BEFORE deleting, so a mid-operation failure can never orphan history.
    /// </summary>
    Task DeleteAsync(string subjectId, CancellationToken cancellationToken);
}
