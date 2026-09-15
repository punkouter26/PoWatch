using PoWatch.Domain.Models;

namespace PoWatch.Application.Contracts;

/// <summary>
/// Append-only history of revision events for a subject. The repository is its own interface so
/// IdentityService can record the audit trail without depending on Table-shape details.
/// </summary>
public interface ISubjectRevisionEventRepository
{
    /// <summary>Append a revision event for a subject.</summary>
    Task AppendAsync(SubjectRevisionEvent revision, CancellationToken cancellationToken);

    /// <summary>Fetch all revision events for a subject, most-recent first. Empty list when the
    /// subject has never been revised (Created events are recorded on first creation, so a
    /// brand-new subject still has one row).</summary>
    Task<IReadOnlyList<SubjectRevisionEvent>> GetHistoryAsync(string subjectId, CancellationToken cancellationToken);
}
