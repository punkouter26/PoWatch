using PoWatch.Domain.Models;

namespace PoWatch.Application.Contracts;

/// <summary>Metadata-only repository for handoff voice memos. The audio bytes live in
/// <see cref="IHandoffMemoStore"/>; this interface is the row-store (Table Storage in
/// production, in-memory in tests).</summary>
public interface IHandoffMemoRepository
{
    Task<HandoffMemo> AddAsync(HandoffMemo memo, CancellationToken cancellationToken);

    Task<HandoffMemo?> GetAsync(string id, CancellationToken cancellationToken);

    /// <summary>Recent memos, most-recent first. Bounded by <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<HandoffMemo>> ListRecentAsync(int limit, CancellationToken cancellationToken);

    /// <summary>All memos older than the cutoff. Used by the purge job.</summary>
    Task<IReadOnlyList<HandoffMemo>> ListOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken);

    Task DeleteAsync(string id, CancellationToken cancellationToken);
}
