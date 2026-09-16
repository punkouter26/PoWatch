using System.Collections.Concurrent;
using PoWatch.Application.Contracts;
using PoWatch.Domain.Models;

namespace PoWatch.Infrastructure.Persistence;

/// <summary>Thread-safe in-memory implementation of <see cref="IHandoffMemoRepository"/>.
/// Production Azure-backed variant belongs alongside AzureSubjectRepository once the rest of
/// the app's table-init pipeline is ready for it.</summary>
public sealed class InMemoryHandoffMemoRepository : IHandoffMemoRepository
{
    private readonly ConcurrentDictionary<string, HandoffMemo> _byId = new(StringComparer.Ordinal);

    public Task<HandoffMemo> AddAsync(HandoffMemo memo, CancellationToken cancellationToken)
    {
        if (!_byId.TryAdd(memo.Id, memo))
        {
            // Collision is a programmer error — the caller is supposed to mint a fresh id per upload.
            throw new InvalidOperationException($"Memo with id '{memo.Id}' already exists.");
        }
        return Task.FromResult(memo);
    }

    public Task<HandoffMemo?> GetAsync(string id, CancellationToken cancellationToken)
    {
        _byId.TryGetValue(id, out var memo);
        return Task.FromResult(memo);
    }

    public Task<IReadOnlyList<HandoffMemo>> ListRecentAsync(int limit, CancellationToken cancellationToken)
    {
        IReadOnlyList<HandoffMemo> snapshot = _byId.Values
            .OrderByDescending(m => m.CreatedAtUtc)
            .Take(Math.Max(0, limit))
            .ToList();
        return Task.FromResult(snapshot);
    }

    public Task<IReadOnlyList<HandoffMemo>> ListOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken)
    {
        IReadOnlyList<HandoffMemo> snapshot = _byId.Values
            .Where(m => m.CreatedAtUtc < cutoffUtc)
            .OrderBy(m => m.CreatedAtUtc)
            .ToList();
        return Task.FromResult(snapshot);
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        _byId.TryRemove(id, out _);
        return Task.CompletedTask;
    }
}
