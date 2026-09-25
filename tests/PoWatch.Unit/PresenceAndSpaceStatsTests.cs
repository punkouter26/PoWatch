using PoWatch.Domain.Models;
using PoWatch.Domain.Services;

namespace PoWatch.Unit;

public sealed class PresenceAndSpaceStatsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 24, 14, 0, 0, TimeSpan.Zero);
    private static readonly Guid SessionId = Guid.Parse("0b1c2d3e-4f50-4617-8293-a4b5c6d7e8f9");

    private static Tick TickAt(int minute, int people, double motion) => new()
    {
        SessionId = SessionId,
        StartUtc = T0.AddMinutes(minute),
        DurationSeconds = 10,
        MotionMean = motion,
        MotionMax = motion,
        LuminanceMean = 0.5,
        Classes = new Dictionary<string, ClassCount> { ["person"] = new(people, people) }
    };

    private static Rollup Minute(int minute, int people, double motion) => Rollup.FromTick(TickAt(minute, people, motion));

    private static SceneEvent Enter(int minute, FrameEdge edge) => new()
    {
        SessionId = SessionId,
        AtUtc = T0.AddMinutes(minute),
        Kind = SceneEventKind.TrackEnter,
        TrackId = $"T{minute}",
        Class = "person",
        Edge = edge
    };

    private static SceneEvent Exit(int minute, FrameEdge edge, double dwellSeconds) => Enter(minute, edge) with
    {
        Kind = SceneEventKind.TrackExit,
        DwellSeconds = dwellSeconds
    };

    [Fact]
    public void Presence_stats_come_out_of_a_minute_series_as_hand_computed()
    {
        // Minutes 0-5 recorded, 6 is a gap (no data), 7-9 recorded.
        //   occupied: 0,1,4,7   empty: 2,3,5,8,9   still (< 0.01): 3,5,8,9   busiest: minute 4 (0.30)
        var series = new List<Rollup>
        {
            Minute(0, 1, 0.05), Minute(1, 2, 0.10), Minute(2, 0, 0.02), Minute(3, 0, 0.005),
            Minute(4, 3, 0.30), Minute(5, 0, 0.001), Minute(7, 1, 0.05), Minute(8, 0, 0.0), Minute(9, 0, 0.0),
            // Events fold into the same minutes: 3 visits, dwells of 30 s, 60 s and 600 s.
            Rollup.FromEvent(Enter(0, FrameEdge.Left)),
            Rollup.FromEvent(Enter(1, FrameEdge.Left)),
            Rollup.FromEvent(Enter(7, FrameEdge.Bottom)),
            Rollup.FromEvent(Exit(1, FrameEdge.Right, 30)),
            Rollup.FromEvent(Exit(4, FrameEdge.Left, 60)),
            Rollup.FromEvent(Exit(9, FrameEdge.Left, 600)),
        };

        var stats = PresenceStats.Summarize(series, TimeSpan.FromMinutes(1));

        Assert.Equal(4.0 / 9, stats.Occupancy, 9);
        Assert.Equal(3, stats.PeakConcurrency);
        // 3 visits over 9 ticks × 10 s = 90 s of coverage → 120 visits per hour.
        Assert.Equal(120, stats.VisitsPerHour, 9);
        // Empty runs: [2,3] = 2 minutes, [5] broken by the gap at 6, [8,9] = 2 → longest 2 minutes.
        Assert.Equal(TimeSpan.FromMinutes(2), stats.LongestEmptyStreak);
        // Still runs: [3], [5], [8,9] → 2 minutes.
        Assert.Equal(TimeSpan.FromMinutes(2), stats.LongestStillStreak);
        Assert.Equal(T0.AddMinutes(4), stats.BusiestStartUtc);
        Assert.Equal(0.30, stats.BusiestMotion, 9);
        // Dwell percentiles come from a log-scale histogram, so they are exact to within one bin (~19%).
        Assert.InRange(stats.DwellP50Seconds!.Value, 60 / 1.2, 60 * 1.2);
        Assert.InRange(stats.DwellP90Seconds!.Value, 600 / 1.2, 600 * 1.2);
        Assert.Equal(600, stats.DwellMaxSeconds);

        var empty = PresenceStats.Summarize([], TimeSpan.FromMinutes(1));
        Assert.Equal(0, empty.Occupancy);
        Assert.Null(empty.DwellP50Seconds);
        Assert.Null(empty.BusiestStartUtc);
    }

    [Fact]
    public void Space_stats_normalise_the_grids_and_rank_the_edges()
    {
        var motion = new float[SpatialGrid.Cells];
        motion[0] = 1f;
        motion[20] = 4f;
        var presence = new float[SpatialGrid.Cells];
        presence[20] = 2f;
        presence[21] = 6f;

        var tick = TickAt(0, 1, 0.1) with { MotionGrid = motion, PresenceGrid = presence };
        var total = new[]
        {
            Rollup.FromTick(tick),
            Rollup.FromTick(tick),
            Rollup.FromEvent(Enter(0, FrameEdge.Left)),
            Rollup.FromEvent(Enter(1, FrameEdge.Left)),
            Rollup.FromEvent(Exit(2, FrameEdge.Right, 10)),
            Rollup.FromEvent(Exit(3, FrameEdge.Left, 10)),
            Rollup.FromEvent(Enter(4, FrameEdge.Top)),
        }.Aggregate(Rollup.Merge);

        var space = SpaceStats.Summarize(total);

        Assert.Equal(1.0, space.MotionHeat[20]);
        Assert.Equal(0.25, space.MotionHeat[0]);
        Assert.Equal(0.0, space.MotionHeat[1]);
        Assert.Equal(1.0, space.PresenceHeat[21]);
        Assert.Equal(20, space.HottestCell);
        Assert.Equal(21, space.FavouriteSpotCell);
        Assert.Equal(2, space.Entries[FrameEdge.Left]);
        Assert.Equal(1, space.Exits[FrameEdge.Right]);
        Assert.Equal(FrameEdge.Left, space.BusiestEdge);

        var blank = SpaceStats.Summarize(Rollup.Empty);
        Assert.Equal(-1, blank.HottestCell);
        Assert.Equal(FrameEdge.None, blank.BusiestEdge);
        Assert.All(blank.MotionHeat, v => Assert.Equal(0, v));
    }
}
