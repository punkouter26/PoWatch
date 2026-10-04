using PoWatch.Domain.Models;
using PoWatch.Domain.Services;

namespace PoWatch.Application.Contracts;

/// <summary>Pre-aggregated minute, hour, day and all-time rollups per user.</summary>
public interface IRollupStore
{
    /// <summary>Merges <paramref name="delta"/> into the bucket that starts at <paramref name="bucketStartUtc"/>.</summary>
    Task MergeAsync(string userId, RollupGrain grain, DateTimeOffset bucketStartUtc, Rollup delta, CancellationToken cancellationToken);

    /// <summary>Buckets of one grain whose start falls in [<paramref name="fromUtc"/>, <paramref name="toUtc"/>), oldest first.</summary>
    Task<IReadOnlyList<Rollup>> GetRangeAsync(string userId, RollupGrain grain, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken);

    /// <summary>Everything the user has ever recorded; <see cref="Rollup.Empty"/> when nothing yet.</summary>
    Task<Rollup> GetAllTimeAsync(string userId, CancellationToken cancellationToken);
}
