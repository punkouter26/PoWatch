using PoWatch.Domain.Models;

namespace PoWatch.Application.Contracts;

/// <summary>Sessions per user, newest first.</summary>
public interface ISessionRepository
{
    /// <summary>Inserts or replaces the session.</summary>
    Task UpsertAsync(Session session, CancellationToken cancellationToken);

    Task<Session?> GetAsync(string userId, Guid sessionId, CancellationToken cancellationToken);

    /// <summary>The user's most recent sessions, newest first.</summary>
    Task<IReadOnlyList<Session>> ListAsync(string userId, int take, CancellationToken cancellationToken);
}

/// <summary>
/// The raw record of what was sensed: every tick and scene event, kept forever and partitioned by
/// the user's local day. Writes are upserts keyed by each item's identity, so a replay rewrites the
/// same rows instead of adding new ones.
/// </summary>
public interface ISensingLog
{
    Task AppendAsync(string userId, DateOnly localDay, IReadOnlyList<Tick> ticks, IReadOnlyList<SceneEvent> events, CancellationToken cancellationToken);

    Task<IReadOnlyList<Tick>> GetTicksAsync(string userId, DateOnly localDay, CancellationToken cancellationToken);

    Task<IReadOnlyList<SceneEvent>> GetEventsAsync(string userId, DateOnly localDay, CancellationToken cancellationToken);
}

/// <summary>
/// Remembers which ingest batches have been folded into the rollups. Rollup merges add, so a
/// replayed batch must be claimed exactly once or it would be counted twice.
/// </summary>
public interface IIngestLedger
{
    /// <summary>True the first time a batch key is claimed for a user; false on every replay.</summary>
    Task<bool> TryClaimAsync(string userId, Guid batchKey, CancellationToken cancellationToken);
}
