using PoWatch.Infrastructure.Persistence;

namespace PoWatch.Unit;

/// <summary>BFF idempotency cache contract. The cache is the load-bearing piece of the offline
/// outbox story: a WiFi blip + retry storm produces many POSTs with the same IdempotencyKey, and
/// the server only mints one ObservationEventId because the second-and-subsequent requests hit
/// the cache. These tests pin the load-bearing TTL behaviour.</summary>
public sealed class IdempotencyCacheTests
{
    [Fact]
    public async Task GetAsync_ReturnsNullForUnknownKey()
    {
        var cache = new InMemoryIdempotencyCache();
        Assert.Null(await cache.GetAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task SetThenGet_RoundTripsTheBody()
    {
        var cache = new InMemoryIdempotencyCache();
        var key = Guid.NewGuid();
        await cache.SetAsync(key, "{\"accepted\":true}", CancellationToken.None);
        Assert.Equal("{\"accepted\":true}", await cache.GetAsync(key, CancellationToken.None));
    }

    [Fact]
    public async Task SetAsync_OverwritesPriorValueForSameKey()
    {
        // A retry storm that races with itself should still produce one cached body, not a stack.
        // The second Set wins; the next Get returns the second body.
        var cache = new InMemoryIdempotencyCache();
        var key = Guid.NewGuid();
        await cache.SetAsync(key, "first", CancellationToken.None);
        await cache.SetAsync(key, "second", CancellationToken.None);
        Assert.Equal("second", await cache.GetAsync(key, CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_ReturnsNullForExpiredKey()
    {
        // A 1-second TTL plus a deliberate past-set exercises the expiry path without
        // depending on real wall-clock waiting. We pre-set with a TTL of 1 second, then
        // wait 1.1s for the entry to fall out of the window. A short sleep is acceptable
        // in a unit test; it is the only deterministic way to exercise the TTL discipline
        // without injecting a time provider.
        var cache = new InMemoryIdempotencyCache(TimeSpan.FromMilliseconds(100));
        var key = Guid.NewGuid();
        await cache.SetAsync(key, "value", CancellationToken.None);

        Assert.Equal("value", await cache.GetAsync(key, CancellationToken.None));
        await Task.Delay(150);
        Assert.Null(await cache.GetAsync(key, CancellationToken.None));
    }

    [Fact]
    public async Task DistinctKeys_AreIndependent()
    {
        var cache = new InMemoryIdempotencyCache();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await cache.SetAsync(a, "alpha", CancellationToken.None);
        await cache.SetAsync(b, "beta", CancellationToken.None);

        Assert.Equal("alpha", await cache.GetAsync(a, CancellationToken.None));
        Assert.Equal("beta", await cache.GetAsync(b, CancellationToken.None));
    }
}
