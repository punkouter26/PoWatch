using PoWatch.Domain.Models;

namespace PoWatch.Domain.Services;

/// <summary>Everything the achievement rules can look at, gathered once per evaluation.</summary>
public sealed record AchievementContext
{
    public required DateTimeOffset NowUtc { get; init; }
    public required TimeZoneInfo Zone { get; init; }
    public int SessionCount { get; init; }
    public TimeSpan LongestSession { get; init; }
    public int DailyStreak { get; init; }
    public Rollup AllTime { get; init; } = Rollup.Empty;
    public Rollup Today { get; init; } = Rollup.Empty;

    /// <summary>Today's hourly rollups, for time-of-day achievements.</summary>
    public IReadOnlyList<Rollup> TodayHourly { get; init; } = [];
}

public sealed record AchievementDefinition(string Id, string Title, string Description, Func<AchievementContext, bool> IsEarned);

public sealed record AchievementUnlock(string AchievementId, DateTimeOffset UnlockedAtUtc);

/// <summary>Stat family E — the trophy cabinet.</summary>
public static class AchievementRules
{
    private static readonly string[] Animals = ["cat", "dog", "bird", "horse", "sheep", "cow", "elephant", "bear", "zebra", "giraffe"];

    public static readonly IReadOnlyList<AchievementDefinition> All =
    [
        new("first-light", "First Light", "Run your first session.", c => c.SessionCount >= 1),
        new("night-owl", "Night Owl", "Someone was around at 3 a.m.", c => OccupiedAtLocalHour(c, 3)),
        new("first-cat", "Here Kitty", "Spot a cat.", c => Seen(c, "cat")),
        new("first-dog", "Good Boy", "Spot a dog.", c => Seen(c, "dog")),
        new("menagerie", "Menagerie", "Spot five different kinds of animal.", c => Animals.Count(a => Seen(c, a)) >= 5),
        new("all-nighter", "All-Nighter", "Keep a session running for eight hours.", c => c.LongestSession >= TimeSpan.FromHours(8)),
        new("crowd", "Crowd", "See five people or animals at once.", c => c.AllTime.PeakConcurrency >= 5),
        new("regular-habits", "Creature of Habit", "Run a session seven days in a row.", c => c.DailyStreak >= 7),
    ];

    /// <summary>
    /// Achievements earned by <paramref name="context"/> that are not already unlocked. Idempotent:
    /// feed the result back into <paramref name="alreadyUnlocked"/> and the next call returns nothing.
    /// </summary>
    public static IReadOnlyList<AchievementUnlock> Evaluate(AchievementContext context, IReadOnlySet<string> alreadyUnlocked)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(alreadyUnlocked);

        return All
            .Where(a => !alreadyUnlocked.Contains(a.Id) && a.IsEarned(context))
            .Select(a => new AchievementUnlock(a.Id, context.NowUtc))
            .ToList();
    }

    private static bool Seen(AchievementContext c, string className) => c.AllTime.Classes.ContainsKey(className);

    private static bool OccupiedAtLocalHour(AchievementContext c, int hour) =>
        c.TodayHourly.Any(r => r.OccupiedTicks > 0 && TimeZoneInfo.ConvertTime(r.BucketStartUtc, c.Zone).Hour == hour);
}
