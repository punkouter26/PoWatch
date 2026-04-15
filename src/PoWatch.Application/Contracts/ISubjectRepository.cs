using PoWatch.Domain.Models;

namespace PoWatch.Application.Contracts;

public interface ISubjectRepository
{
    Task<IReadOnlyList<SubjectProfile>> GetAllAsync(CancellationToken cancellationToken);

    Task<SubjectProfile> GetOrCreateAsync(string? hint, CancellationToken cancellationToken);

    Task<SubjectProfile?> GetByIdAsync(string subjectId, CancellationToken cancellationToken);

    Task<SubjectProfile> RenameAsync(string subjectId, string newDisplayName, CancellationToken cancellationToken);

    Task<SubjectProfile> MergeAsync(string primarySubjectId, string secondarySubjectId, string? explicitName, CancellationToken cancellationToken);
}
