using System.Collections.Concurrent;
using PoWatch.Application.Contracts;
using PoWatch.Domain.Models;
using PoWatch.Domain.Services;

namespace PoWatch.Infrastructure.Persistence;

// In-memory stand-ins used when no storage account is configured (and by tests). They follow the
// same keying rules as the Azure stores so replays behave identically.

public sealed class InMemorySessionRepository : ISessionRepository
{
    private readonly ConcurrentDictionary<(string UserId, Guid Id), Session> _sessions = new();

    public Task UpsertAsync(Session session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        _sessions[(session.UserId, session.Id)] = session;
        return Task.CompletedTask;
    }

    public Task<Session?> GetAsync(string userId, Guid sessionId, CancellationToken cancellationToken) =>
        Task.FromResult(_sessions.GetValueOrDefault((userId, sessionId)));

    public Task<IReadOnlyList<Session>> ListAsync(string userId, int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Session>>(_sessions.Values
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.StartedUtc)
            .Take(take)
            .ToList());
}

public sealed class InMemorySensingLog : ISensingLog
{
    private readonly ConcurrentDictionary<(string UserId, DateOnly Day, string RowKey), Tick> _ticks = new();
    private readonly ConcurrentDictionary<(string UserId, DateOnly Day, Guid Id), SceneEvent> _events = new();

    public Task AppendAsync(string userId, DateOnly localDay, IReadOnlyList<Tick> ticks, IReadOnlyList<SceneEvent> events, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticks);
        ArgumentNullException.ThrowIfNull(events);
        foreach (var tick in ticks) _ticks[(userId, localDay, SensingKeys.TickRowKey(tick))] = tick;
        foreach (var sceneEvent in events) _events[(userId, localDay, sceneEvent.StableId())] = sceneEvent;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Tick>> GetTicksAsync(string userId, DateOnly localDay, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Tick>>(_ticks
            .Where(kv => kv.Key.UserId == userId && kv.Key.Day == localDay)
            .Select(kv => kv.Value)
            .OrderBy(t => t.StartUtc)
            .ToList());

    public Task<IReadOnlyList<SceneEvent>> GetEventsAsync(string userId, DateOnly localDay, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SceneEvent>>(_events
            .Where(kv => kv.Key.UserId == userId && kv.Key.Day == localDay)
            .Select(kv => kv.Value)
            .OrderBy(e => e.AtUtc)
            .ToList());
}

public sealed class InMemoryIngestLedger : IIngestLedger
{
    private readonly ConcurrentDictionary<(string UserId, Guid BatchKey), byte> _claimed = new();

    public Task<bool> TryClaimAsync(string userId, Guid batchKey, CancellationToken cancellationToken) =>
        Task.FromResult(_claimed.TryAdd((userId, batchKey), 0));
}

public sealed class InMemoryRollupStore : IRollupStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<(string UserId, RollupGrain Grain, DateTimeOffset Start), Rollup> _buckets = [];

    public Task MergeAsync(string userId, RollupGrain grain, DateTimeOffset bucketStartUtc, Rollup delta, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var key = (userId, grain, bucketStartUtc);
            var existing = _buckets.GetValueOrDefault(key, Rollup.Empty);
            _buckets[key] = Rollup.Merge(existing, delta) with { BucketStartUtc = bucketStartUtc };
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Rollup>> GetRangeAsync(string userId, RollupGrain grain, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<Rollup>>(_buckets
                .Where(kv => kv.Key.UserId == userId && kv.Key.Grain == grain && kv.Key.Start >= fromUtc && kv.Key.Start < toUtc)
                .Select(kv => kv.Value)
                .OrderBy(r => r.BucketStartUtc)
                .ToList());
        }
    }

    public Task<Rollup> GetAllTimeAsync(string userId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_buckets.GetValueOrDefault((userId, RollupGrain.AllTime, DateTimeOffset.UnixEpoch), Rollup.Empty));
        }
    }
}

public sealed class InMemoryAchievementStore : IAchievementStore
{
    private readonly ConcurrentDictionary<(string UserId, string Id), DateTimeOffset> _unlocked = new();
    private readonly ConcurrentDictionary<(string UserId, string Id), RecordEntry> _records = new();

    public Task<IReadOnlyDictionary<string, DateTimeOffset>> GetUnlockedAsync(string userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, DateTimeOffset>>(_unlocked
            .Where(kv => kv.Key.UserId == userId)
            .ToDictionary(kv => kv.Key.Id, kv => kv.Value, StringComparer.Ordinal));

    public Task UnlockAsync(string userId, IReadOnlyList<AchievementUnlock> unlocks, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unlocks);
        foreach (var unlock in unlocks) _unlocked.TryAdd((userId, unlock.AchievementId), unlock.UnlockedAtUtc);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, RecordEntry>> GetRecordsAsync(string userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, RecordEntry>>(_records
            .Where(kv => kv.Key.UserId == userId)
            .ToDictionary(kv => kv.Key.Id, kv => kv.Value, StringComparer.Ordinal));

    public Task SaveRecordsAsync(string userId, IReadOnlyList<RecordEntry> records, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);
        foreach (var record in records) _records[(userId, record.RecordId)] = record;
        return Task.CompletedTask;
    }
}

/// <summary>Row keys shared by the in-memory and Azure sensing logs.</summary>
public static class SensingKeys
{
    /// <summary>Time-ordered and unique per session: two sessions can tick in the same instant.</summary>
    public static string TickRowKey(Tick tick)
    {
        ArgumentNullException.ThrowIfNull(tick);
        return $"{tick.StartUtc.UtcTicks:D19}-{tick.SessionId:N}";
    }
}
