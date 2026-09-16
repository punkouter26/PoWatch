using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PoWatch.Application.Contracts;

namespace PoWatch.Infrastructure.Persistence;

/// <summary>Thread-safe in-memory implementation of <see cref="IIdempotencyCache"/>. Production
/// Azure-backed variant (Table Storage with a TTL) belongs alongside AzureSubjectRepository
/// once the storage pipeline is ready. The in-memory implementation is the right shape for
/// unit tests and local Azurite runs; its TTL discipline is the same as the production one,
/// so contract tests pass either way.</summary>
public sealed class InMemoryIdempotencyCache : IIdempotencyCache, IDisposable
{
    private readonly ConcurrentDictionary<Guid, (string Body, DateTimeOffset ExpiresAtUtc)> _byKey = new();
    private readonly TimeSpan _ttl;
    private readonly ILogger<InMemoryIdempotencyCache>? _logger;
    private readonly System.Timers.Timer? _sweeper;

    public InMemoryIdempotencyCache(TimeSpan? ttl = null, ILogger<InMemoryIdempotencyCache>? logger = null)
    {
        _ttl = ttl ?? TimeSpan.FromMinutes(10);
        _logger = logger;
        _sweeper = new System.Timers.Timer(TimeSpan.FromMinutes(1).TotalMilliseconds) { AutoReset = true };
        _sweeper.Elapsed += (_, _) => Sweep();
        _sweeper.Start();
    }

    public Task<string?> GetAsync(Guid idempotencyKey, CancellationToken cancellationToken)
    {
        if (_byKey.TryGetValue(idempotencyKey, out var entry))
        {
            if (entry.ExpiresAtUtc > DateTimeOffset.UtcNow)
            {
                return Task.FromResult<string?>(entry.Body);
            }
            _byKey.TryRemove(idempotencyKey, out _);
        }
        return Task.FromResult<string?>(null);
    }

    public Task SetAsync(Guid idempotencyKey, string responseBody, CancellationToken cancellationToken)
    {
        _byKey[idempotencyKey] = (responseBody, DateTimeOffset.UtcNow.Add(_ttl));
        return Task.CompletedTask;
    }

    private void Sweep()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kvp in _byKey)
        {
            if (kvp.Value.ExpiresAtUtc <= now)
            {
                _byKey.TryRemove(kvp.Key, out _);
            }
        }
    }

    public void Dispose()
    {
        _sweeper?.Stop();
        _sweeper?.Dispose();
    }
}
