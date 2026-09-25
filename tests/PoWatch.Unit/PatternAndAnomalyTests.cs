using PoWatch.Domain.Models;
using PoWatch.Domain.Services;

namespace PoWatch.Unit;

public sealed class PatternAndAnomalyTests
{
    private static readonly TimeZoneInfo UtcMinus5 =
        TimeZoneInfo.CreateCustomTimeZone("Test/UTC-5", TimeSpan.FromHours(-5), "UTC-5", "UTC-5");

    // Local Monday 2026-09-21 00:00 at UTC-5.
    private static readonly DateTimeOffset MondayLocalMidnightUtc = new(2026, 9, 21, 5, 0, 0, TimeSpan.Zero);

    private static Rollup Hour(int dayOffset, int localHour, double motion) =>
        Rollup.FromTick(new Tick
        {
            SessionId = Guid.NewGuid(),
            StartUtc = MondayLocalMidnightUtc.AddDays(dayOffset).AddHours(localHour),
            MotionMean = motion,
            MotionMax = motion
        });

    [Fact]
    public void Hourly_patterns_bucket_by_local_hour_and_weekday()
    {
        // Two Mondays at 09:00 (0.2 and 0.4) and one Tuesday at 18:00 (0.5).
        var hourly = new[] { Hour(0, 9, 0.2), Hour(7, 9, 0.4), Hour(1, 18, 0.5) };

        var grid = PatternStats.HourByWeekday(hourly, UtcMinus5, r => r.Motion.Mean);

        Assert.Equal(0.3, grid[(int)DayOfWeek.Monday][9], 12);
        Assert.Equal(0.5, grid[(int)DayOfWeek.Tuesday][18], 12);
        Assert.Equal(0, grid[(int)DayOfWeek.Sunday][9]);

        var profile = PatternStats.HourlyProfile([Hour(0, 9, 0.2), Hour(0, 10, 0.6)], UtcMinus5, r => r.Motion.Mean);
        Assert.Equal(24, profile.Count);
        Assert.Equal(0.6, profile[10]);

        // Busiest remaining hour on a Monday, asked at 08:30 local: 09:00 beats 07:00 (already past).
        var history = new[] { Hour(0, 7, 0.9), Hour(0, 9, 0.4), Hour(0, 20, 0.1), Hour(7, 9, 0.6) };
        var asked = MondayLocalMidnightUtc.AddDays(14).AddHours(8.5);
        Assert.Equal(9, PatternStats.NextBusyHour(history, UtcMinus5, asked, r => r.Motion.Mean));
        Assert.Null(PatternStats.NextBusyHour(history, UtcMinus5, asked.AddHours(15), r => r.Motion.Mean));
    }

    [Fact]
    public void Anomaly_math_scores_today_against_the_usual()
    {
        // Rhythm: identical shape → 1, inverted → -1, flat → undefined.
        double[] usual = [0, 1, 2, 3];
        Assert.Equal(1, AnomalyMath.RhythmScore([0, 2, 4, 6], usual)!.Value, 12);
        Assert.Equal(-1, AnomalyMath.RhythmScore([3, 2, 1, 0], usual)!.Value, 12);
        Assert.Null(AnomalyMath.RhythmScore([1, 1, 1, 1], usual));

        // Baseline 10, 12, 14 → mean 12, sample sd 2. Today 17 → z = 2.5, flagged.
        var anomaly = AnomalyMath.Compare("visits", today: 17, baseline: [10, 12, 14]);
        Assert.Equal(12, anomaly.Mean);
        Assert.Equal(2, anomaly.StdDev, 12);
        Assert.Equal(2.5, anomaly.Z!.Value, 12);
        Assert.True(anomaly.Flagged);
        Assert.False(AnomalyMath.Compare("visits", 13, [10, 12, 14]).Flagged);
        Assert.Null(AnomalyMath.Compare("visits", 13, [12, 12, 12]).Z);
        Assert.Null(AnomalyMath.Compare("visits", 13, []).Z);

        // Least-squares slope per day: 1, 3, 5, 7 → +2/day; too little data → 0.
        Assert.Equal(2, AnomalyMath.TrendSlope([1, 3, 5, 7]), 12);
        Assert.Equal(0, AnomalyMath.TrendSlope([4]));
    }
}
