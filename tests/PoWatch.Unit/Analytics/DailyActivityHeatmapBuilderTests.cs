using PoWatch.Shared.Models;
using PoWatch.Shared.Services.Analytics;

namespace PoWatch.Unit.Analytics;

/// <summary>
/// Verifies the 24-hour density strip builder. The three-tier bucketing is what makes the
/// strip usable — a busy hour of routine activity must not drown out a quiet hour of urgent
/// outliers. These tests pin the bucketing contract so the Live Room strip can never silently
/// regress to "everything counts as one bar height".
/// </summary>
public sealed class DailyActivityHeatmapBuilderTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    [Fact]
    public void Empty_input_yields_zero_everywhere()
    {
        var result = DailyActivityHeatmapBuilder.Build([], new DateOnly(2026, 8, 30), Utc);

        Assert.Equal(0, result.TotalEvents);
        Assert.All(result.Routine, v => Assert.Equal(0, v));
        Assert.All(result.Notable, v => Assert.Equal(0, v));
        Assert.All(result.Urgent, v => Assert.Equal(0, v));
    }

    [Fact]
    public void Events_are_split_into_routine_notable_and_urgent_tiers()
    {
        var today = new DateOnly(2026, 8, 30);
        var events = new[]
        {
            Event(today, 8, isSignificant: false, isOutlier: false),
            Event(today, 8, isSignificant: false, isOutlier: false),
            Event(today, 14, isSignificant: true,  isOutlier: false),
            Event(today, 22, isSignificant: true,  isOutlier: true),
        };

        var result = DailyActivityHeatmapBuilder.Build(events, today, Utc);

        Assert.Equal(4, result.TotalEvents);
        Assert.Equal(2, result.Routine[8]);
        Assert.Equal(0, result.Notable[8]);
        Assert.Equal(1, result.Notable[14]);
        Assert.Equal(1, result.Urgent[22]);
    }

    [Fact]
    public void Events_on_a_different_day_are_ignored()
    {
        var today = new DateOnly(2026, 8, 30);
        var yesterday = new DateOnly(2026, 8, 29);
        var events = new[]
        {
            Event(yesterday, 8, isSignificant: true, isOutlier: false),
            Event(today,     9, isSignificant: true, isOutlier: false),
        };

        var result = DailyActivityHeatmapBuilder.Build(events, today, Utc);

        Assert.Equal(1, result.TotalEvents);
        Assert.Equal(1, result.Notable[9]);
        Assert.Equal(0, result.Notable[8]);
    }

    [Fact]
    public void An_outlier_event_does_not_also_count_as_notable()
    {
        // The archives narrative double-counted outliers as both "notable" and "unusual" on the
        // Live Room — two counters that always moved together and so gave no information. The
        // builder must not let an outlier fall into the notable bucket too.
        var today = new DateOnly(2026, 8, 30);
        var events = new[]
        {
            Event(today, 4, isSignificant: true, isOutlier: true),
        };

        var result = DailyActivityHeatmapBuilder.Build(events, today, Utc);

        Assert.Equal(1, result.Urgent[4]);
        Assert.Equal(0, result.Notable[4]);
    }

    [Fact]
    public void Normalise_caps_at_one()
    {
        Assert.Equal(1d, DailyActivityHeatmapBuilder.Normalise(20, 6));
        Assert.Equal(0.5d, DailyActivityHeatmapBuilder.Normalise(1, 2));
        Assert.Equal(0d, DailyActivityHeatmapBuilder.Normalise(0, 5));
        Assert.Equal(0d, DailyActivityHeatmapBuilder.Normalise(5, 0));
    }

    /// <summary>Event with the given hour and significance flags, dated on <paramref name="day"/>.
    /// The <see cref="Utc"/> zone is fixed so the test result is independent of the runner's
    /// local timezone.</summary>
    private static ObservationEventDto Event(DateOnly day, int hour, bool isSignificant, bool isOutlier) => new()
    {
        Id = Guid.NewGuid(),
        ObservedAtUtc = new DateTimeOffset(day.Year, day.Month, day.Day, hour, 30, 0, TimeSpan.Zero),
        Activity = "test",
        IsSignificant = isSignificant,
        IsClinicalOutlier = isOutlier,
    };
}
