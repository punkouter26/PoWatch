namespace PoWatch.Application.Contracts;

/// <summary>Short-lived cache of prior ingest responses keyed by IdempotencyKey. The cache is
/// intentionally narrow: only ingest results, and only for ~10 minutes. Anything longer than
/// that would be persistence, not a cache.</summary>
public interface IIdempotencyCache
{
    /// <summary>Returns a previously stored response for the key, or null if absent or expired.</summary>
    Task<string?> GetAsync(Guid idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Store the response body for the key. The implementation is responsible for TTL.</summary>
    Task SetAsync(Guid idempotencyKey, string responseBody, CancellationToken cancellationToken);
}
