using PoWatch.Domain.Models;
using PoWatch.Domain.Services;

namespace PoWatch.Unit;

public sealed class AchievementAndRecordTests
{
    private static readonly TimeZoneInfo UtcMinus5 =
        TimeZoneInfo.CreateCustomTimeZone("Test/UTC-5", TimeSpan.FromHours(-5), "UTC-5", "UTC-5");

    // 03:00 local on 2026-09-24 at UTC-5.
    private static readonly DateTimeOffset ThreeAmLocal = new(2026, 9, 24, 8, 0, 0, TimeSpan.Zero);

    private static readonly string[] FiveAnimals = ["cat", "dog", "bird", "horse", "sheep"];

    private static Rollup Occupied(DateTimeOffset startUtc, params (string Class, int Count)[] classes) =>
        Rollup.FromTick(new Tick
        {
            SessionId = Guid.NewGuid(),
            StartUtc = startUtc,
            PixelSamples = 50,
            Classes = classes.ToDictionary(c => c.Class, c => new ClassCount(c.Count, c.Count))
        });

    [Fact]
    public void Achievements_unlock_once_from_what_the_stats_say()
    {
        Assert.Equal(8, AchievementRules.All.Count);
        Assert.Equal(AchievementRules.All.Count, AchievementRules.All.Select(a => a.Id).Distinct().Count());

        var nightOwlHour = Occupied(ThreeAmLocal, ("person", 1), ("cat", 1));
        var animals = FiveAnimals
            .Select(a => Occupied(ThreeAmLocal.AddHours(-30), (a, 1)))
            .Aggregate(Rollup.Merge);
        var context = new AchievementContext
        {
            NowUtc = ThreeAmLocal.AddMinutes(30),
            Zone = UtcMinus5,
            SessionCount = 1,
            LongestSession = TimeSpan.FromHours(9),
            AllTime = Rollup.Merge(nightOwlHour, animals),
            TodayHourly = [nightOwlHour]
        };

        var unlocked = AchievementRules.Evaluate(context, alreadyUnlocked: new HashSet<string>());
        var ids = unlocked.Select(u => u.AchievementId).ToHashSet();

        Assert.Contains("first-light", ids);
        Assert.Contains("night-owl", ids);
        Assert.Contains("menagerie", ids);
        Assert.Contains("first-cat", ids);
        Assert.Contains("all-nighter", ids);
        Assert.DoesNotContain("crowd", ids);
        Assert.All(unlocked, u => Assert.Equal(context.NowUtc, u.UnlockedAtUtc));

        // Evaluating again with the same stats unlocks nothing new.
        Assert.Empty(AchievementRules.Evaluate(context, ids));

        // Nothing observed yet: nothing earned.
        var blank = new AchievementContext { NowUtc = context.NowUtc, Zone = UtcMinus5 };
        Assert.Empty(AchievementRules.Evaluate(blank, new HashSet<string>()));
    }

    [Fact]
    public void Records_only_move_when_beaten()
    {
        var at = ThreeAmLocal;
        var records = new Dictionary<string, RecordEntry>();

        var first = RecordRules.Update(records, [new("longest-session", 40, at), new("peak-concurrency", 3, at)]);
        Assert.Equal(2, first.Count);
        foreach (var entry in first) records[entry.RecordId] = entry;

        // A lower or equal value is not a new record; a higher one is.
        var second = RecordRules.Update(records, [new("longest-session", 40, at.AddDays(1)), new("peak-concurrency", 5, at.AddDays(1))]);
        var beaten = Assert.Single(second);
        Assert.Equal("peak-concurrency", beaten.RecordId);
        Assert.Equal(5, beaten.Value);
        Assert.Equal(3, beaten.PreviousValue);

        // Replaying the same candidates is a no-op once applied.
        records[beaten.RecordId] = beaten;
        Assert.Empty(RecordRules.Update(records, [new("peak-concurrency", 5, at.AddDays(1))]));
    }
}
