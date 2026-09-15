using System.Collections.Concurrent;
using PoWatch.Application.Contracts;
using PoWatch.Domain.Models;

namespace PoWatch.Infrastructure.Persistence;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="ISubjectRevisionEventRepository"/>.
/// Mirrors the same dual-repository pattern that <see cref="InMemorySubjectRepository"/> and
/// <c>AzureSubjectRepository</c> use for the subject profile rows — production code keeps an
/// Azure-backed variant for when persistence across restarts matters.
/// </summary>
public sealed class InMemorySubjectRevisionEventRepository : ISubjectRevisionEventRepository
{
    private readonly ConcurrentDictionary<string, List<SubjectRevisionEvent>> _bySubject = new(StringComparer.OrdinalIgnoreCase);

    public Task AppendAsync(SubjectRevisionEvent revision, CancellationToken cancellationToken)
    {
        // List.Add is not thread-safe; lock the per-subject bucket so concurrent appends against
        // the same subject (e.g. two merges racing) don't corrupt the list.
        var bucket = _bySubject.GetOrAdd(revision.SubjectId.Value, _ => []);
        lock (bucket)
        {
            bucket.Add(revision);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SubjectRevisionEvent>> GetHistoryAsync(string subjectId, CancellationToken cancellationToken)
    {
        if (!_bySubject.TryGetValue(subjectId, out var bucket))
        {
            return Task.FromResult<IReadOnlyList<SubjectRevisionEvent>>(Array.Empty<SubjectRevisionEvent>());
        }

        // Reverse-chronological: most recent first. The history pane reads top-to-bottom so the
        // latest event lands at the top. A stable secondary sort on Kind keeps equal-timestamp rows
        // (the merge-all-at-once case writes three rows with the same timestamp) in deterministic order.
        IReadOnlyList<SubjectRevisionEvent> snapshot;
        lock (bucket)
        {
            snapshot = bucket
                .OrderByDescending(e => e.OccurredAtUtc)
                .ThenBy(e => (int)e.Kind)
                .ToList();
        }
        return Task.FromResult(snapshot);
    }
}
