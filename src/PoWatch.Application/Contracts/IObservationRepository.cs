using PoWatch.Domain.Models;

namespace PoWatch.Application.Contracts;

/// <summary>
/// Repository for observation events storage and retrieval.
/// Supports both Azure Table Storage and in-memory implementations.
/// </summary>
public interface IObservationRepository
{
    /// <summary>
    /// Adds a new observation event to storage.
    /// </summary>
    Task AddAsync(ObservationEvent observation, CancellationToken cancellationToken);

    /// <summary>
    /// Gets all observation events for a specific date.
    /// </summary>
    Task<IReadOnlyList<ObservationEvent>> GetByDateAsync(DateOnly date, CancellationToken cancellationToken);

    /// <summary>
    /// Gets all observation events within a date range (inclusive).
    /// </summary>
    Task<IReadOnlyList<ObservationEvent>> GetByDateRangeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>
    /// Gets observation events for a specific subject within a date range.
    /// This is more efficient than loading all events and filtering in memory.
    /// </summary>
    /// <param name="subjectId">The subject ID to filter by.</param>
    /// <param name="from">Start date (inclusive).</param>
    /// <param name="to">End date (inclusive).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Filtered observation events for the subject.</returns>
    Task<IReadOnlyList<ObservationEvent>> GetBySubjectAndDateRangeAsync(
        string subjectId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets the most recent observation event for a subject.
    /// </summary>
    Task<ObservationEvent?> GetLatestForSubjectAsync(string subjectId, CancellationToken cancellationToken);

    /// <summary>
    /// Rewrites all observation events for a subject to a new subject ID.
    /// Used after subject rename/merge operations.
    /// </summary>
    /// <returns>The number of events rewritten.</returns>
    Task<int> MergeSubjectAsync(string oldSubjectId, SubjectProfile target, CancellationToken cancellationToken);
}
