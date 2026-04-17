using PoWatch.Domain.Models;

namespace PoWatch.Application.Contracts;

public interface IObservationRepository
{
    Task AddAsync(ObservationEvent observation, CancellationToken cancellationToken);

    Task<IReadOnlyList<ObservationEvent>> GetByDateAsync(DateOnly date, CancellationToken cancellationToken);

    /// <summary>Returns all observations within an inclusive date range, ordered by ObservedAtUtc ascending.</summary>
    Task<IReadOnlyList<ObservationEvent>> GetByDateRangeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

    Task<ObservationEvent?> GetLatestForSubjectAsync(string subjectId, CancellationToken cancellationToken);

    Task<int> MergeSubjectAsync(string oldSubjectId, SubjectProfile target, CancellationToken cancellationToken);
}
