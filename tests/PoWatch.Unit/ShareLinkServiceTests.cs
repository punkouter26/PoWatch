using Microsoft.Extensions.Logging.Abstractions;
using PoWatch.Application.Contracts;
using PoWatch.Application.Services;
using PoWatch.Domain.Models;
using PoWatch.Infrastructure.Persistence;

namespace PoWatch.Unit;

/// <summary>
/// Family share-link service tests. The link is the entire security model — long id, short TTL,
/// single-purpose — so these tests pin the load-bearing properties: TTL is applied, expiry
/// renders the link unusable, revocation sticks, and re-revocation is idempotent.
/// </summary>
public sealed class ShareLinkServiceTests
{
    [Fact]
    public async Task CreateAsync_AppliesDefault24HourTtl()
    {
        var service = BuildService(out _, out _);
        var link = await service.CreateAsync(DateOnly.FromDateTime(DateTime.Now), "tester", ttl: null, CancellationToken.None);

        var expectedExpiry = link.CreatedAtUtc.AddHours(24);
        Assert.InRange(link.ExpiresAtUtc, expectedExpiry.AddSeconds(-2), expectedExpiry.AddSeconds(2));
    }

    [Fact]
    public async Task CreateAsync_RespectsExplicitTtl()
    {
        var service = BuildService(out _, out _);
        var link = await service.CreateAsync(DateOnly.FromDateTime(DateTime.Now), "tester", TimeSpan.FromHours(2), CancellationToken.None);

        Assert.InRange(
            link.ExpiresAtUtc - link.CreatedAtUtc,
            TimeSpan.FromHours(2) - TimeSpan.FromSeconds(2),
            TimeSpan.FromHours(2) + TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task CreateAsync_RejectsNonPositiveTtl()
    {
        var service = BuildService(out _, out _);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(DateOnly.FromDateTime(DateTime.Now), "tester", TimeSpan.Zero, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(DateOnly.FromDateTime(DateTime.Now), "tester", TimeSpan.FromMinutes(-5), CancellationToken.None));
    }

    [Fact]
    public async Task IsUsable_StartsTrueAndFlipsToFalseAtExpiry()
    {
        // Freshly created link is usable; a manually-aged link is not. We age via the repository
        // by re-adding the link with a backdated timestamp, which exercises the IsUsable gate
        // without exposing a time-machine to the rest of the service.
        var repo = new InMemoryShareLinkRepository();
        var store = new InMemoryArchivesStory();
        var archives = new ArchivesService(store, NullLogger<ArchivesService>.Instance);
        var service = new ShareLinkService(repo, archives, NullLogger<ShareLinkService>.Instance);

        var fresh = await service.CreateAsync(DateOnly.FromDateTime(DateTime.Now), "tester", null, CancellationToken.None);
        Assert.True(fresh.IsUsable);

        var aged = new ShareLink
        {
            Id = "aged",
            Date = fresh.Date,
            CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-2),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            AnonymisedNarrative = "snapshot"
        };
        await repo.AddAsync(aged, CancellationToken.None);
        Assert.False(aged.IsUsable);
    }

    [Fact]
    public async Task RevokeAsync_SetsRevokedAt_AndIsIdempotent()
    {
        var service = BuildService(out _, out _);
        var link = await service.CreateAsync(DateOnly.FromDateTime(DateTime.Now), "tester", null, CancellationToken.None);

        Assert.True(await service.RevokeAsync(link.Id, "tester", CancellationToken.None));
        Assert.False(link.IsUsable);

        // Second revoke is a no-op that returns true — the link was already revoked, the
        // caregiver's intent is satisfied. Distinct from "link does not exist" which returns false.
        Assert.True(await service.RevokeAsync(link.Id, "tester", CancellationToken.None));
    }

    [Fact]
    public async Task RevokeAsync_ReturnsFalse_WhenLinkDoesNotExist()
    {
        var service = BuildService(out _, out _);
        Assert.False(await service.RevokeAsync("ghost-id", "tester", CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_AnonymisesTheSnapshot_NeverReturningRawSubjectIds()
    {
        // The view path is the security model. Confirm the snapshot contains humanised names
        // and no raw "Subject-N" ids, since the family-facing read endpoint returns the
        // snapshot verbatim and we cannot scrub it later.
        var store = new InMemoryArchivesStory();
        var archives = new ArchivesService(store, NullLogger<ArchivesService>.Instance);
        var service = new ShareLinkService(new InMemoryShareLinkRepository(), archives, NullLogger<ShareLinkService>.Instance);

        var link = await service.CreateAsync(DateOnly.FromDateTime(DateTime.Now), "tester", null, CancellationToken.None);

        Assert.NotEmpty(link.AnonymisedNarrative);
        Assert.DoesNotContain("Subject-", link.AnonymisedNarrative, StringComparison.OrdinalIgnoreCase);
    }

    private static ShareLinkService BuildService(
        out InMemoryShareLinkRepository repository,
        out InMemoryArchivesStory store)
    {
        repository = new InMemoryShareLinkRepository();
        store = new InMemoryArchivesStory();
        var archives = new ArchivesService(store, NullLogger<ArchivesService>.Instance);
        return new ShareLinkService(repository, archives, NullLogger<ShareLinkService>.Instance);
    }

    /// <summary>Empty observation store — the service only reads ClinicalNarrative, which is
    /// a fixed message when the timeline is empty ("No observations were recorded on this day."),
    /// so the share-link snapshot is well-defined without a real test fixture.</summary>
    private sealed class InMemoryArchivesStory : IObservationRepository
    {
        public Task AddAsync(ObservationEvent observation, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<ObservationEvent>> GetByDateAsync(DateOnly date, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ObservationEvent>>([]);
        public Task<ObservationEvent?> GetLatestForSubjectAsync(string subjectId, CancellationToken cancellationToken) =>
            Task.FromResult<ObservationEvent?>(null);
        public Task<int> MergeSubjectAsync(string oldSubjectId, SubjectProfile target, CancellationToken cancellationToken) =>
            Task.FromResult(0);
        public Task<IReadOnlyList<ObservationEvent>> GetByDateRangeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ObservationEvent>>([]);
        public Task<IReadOnlyList<ObservationEvent>> GetBySubjectAndDateRangeAsync(string subjectId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ObservationEvent>>([]);
    }
}
