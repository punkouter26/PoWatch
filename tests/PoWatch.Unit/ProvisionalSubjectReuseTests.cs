using PoWatch.Domain.Models;
using PoWatch.Infrastructure.Persistence;

namespace PoWatch.Unit;

/// <summary>
/// A hint-less observation used to mint a brand-new Subject-N every time, so one person sitting in
/// the room became a new "person" on every cycle and People filled with un-nameable duplicates.
/// </summary>
public sealed class ProvisionalSubjectReuseTests
{
    private static InMemorySubjectRepository Repo(int windowSeconds = 1800) =>
        new(TimeSpan.FromSeconds(windowSeconds));

    [Fact]
    public async Task Hintless_observations_reuse_the_same_provisional_subject()
    {
        var repo = Repo();

        var first = await repo.GetOrCreateAsync(null, CancellationToken.None);
        var second = await repo.GetOrCreateAsync(null, CancellationToken.None);
        var third = await repo.GetOrCreateAsync(null, CancellationToken.None);

        Assert.Equal(first.SubjectId, second.SubjectId);
        Assert.Equal(first.SubjectId, third.SubjectId);
        Assert.Single(await repo.GetAllAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Reuse_can_be_disabled_by_zeroing_the_window()
    {
        var repo = Repo(windowSeconds: 0);

        var first = await repo.GetOrCreateAsync(null, CancellationToken.None);
        var second = await repo.GetOrCreateAsync(null, CancellationToken.None);

        Assert.NotEqual(first.SubjectId, second.SubjectId);
    }

    [Fact]
    public async Task A_named_subject_is_never_silently_reused_for_an_unknown_person()
    {
        var repo = Repo();

        var provisional = await repo.GetOrCreateAsync(null, CancellationToken.None);
        await repo.RenameAsync(provisional.SubjectId.Value, "Alice", CancellationToken.None);

        // The only subject on file is now Known, so a hint-less sighting must start a fresh
        // provisional identity rather than attributing the activity to Alice.
        var next = await repo.GetOrCreateAsync(null, CancellationToken.None);

        Assert.Equal(IdentityStatus.Temporary, next.IdentityStatus);
        Assert.NotEqual("Alice", next.DisplayName);
    }

    [Fact]
    public async Task An_explicit_hint_still_wins_over_provisional_reuse()
    {
        var repo = Repo();

        await repo.GetOrCreateAsync(null, CancellationToken.None);
        var hinted = await repo.GetOrCreateAsync("Bob", CancellationToken.None);

        Assert.Equal("Bob", hinted.DisplayName);
        Assert.Equal(IdentityStatus.Known, hinted.IdentityStatus);
    }
}
