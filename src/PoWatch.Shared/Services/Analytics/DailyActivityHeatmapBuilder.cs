using PoWatch.Shared.Models;

namespace PoWatch.Shared.Services.Analytics;

/// <summary>
/// Pure (No-I/O) builder for the 24-hour activity heatmap rendered on the Live Room.
/// Buckets events into routine / notable / urgent tiers per local hour so the strip
/// can render three independent intensities rather than a flat count that hides
/// quiet hours of notable events inside busy hours of routine ones.
/// </summary>
/// <remarks>
/// Input is any IReadOnlyList of <see cref="ObservationEventDto"/>; output is a
/// <see cref="DailyActivityBucketsDto"/> ready to feed the SVG/CSS heatmap renderer.
/// This class is deliberately allocation-light: it builds three fixed-size int[24]
/// buffers per call. The Live Room calls it on every refresh cycle, so we avoid
/// LINQ in the hot path.
/// </remarks>
public static class DailyActivityHeatmapBuilder
{
    /// <summary>
    /// Build the per-hour bucket vector for the local day of <paramref name="day"/>.
    /// Events with <c>ObservedAtUtc</c> on a different local date fall into the
    /// "other" bucket and are silently dropped — they belong to a different heatmap.
    /// </summary>
    public static DailyActivityBucketsDto Build(
        IReadOnlyList<ObservationEventDto> events,
        DateOnly day,
        TimeZoneInfo? timeZone = null)
    {
        timeZone ??= TimeZoneInfo.Local;

        var routine = new int[24];
        var notable = new int[24];
        var urgent = new int[24];

        for (var i = 0; i < events.Count; i++)
        {
            var ev = events[i];
            var local = TimeZoneInfo.ConvertTime(ev.ObservedAtUtc, timeZone);
            if (DateOnly.FromDateTime(local.DateTime) != day) continue;

            var hour = local.Hour;
            if (hour is < 0 or > 23) continue;

            if (ev.IsClinicalOutlier)
            {
                urgent[hour]++;
            }
            else if (ev.IsSignificant)
            {
                notable[hour]++;
            }
            else
            {
                routine[hour]++;
            }
        }

        var total = routine.Sum() + notable.Sum() + urgent.Sum();
        return new DailyActivityBucketsDto
        {
            Date = day,
            Routine = routine,
            Notable = notable,
            Urgent = urgent,
            TotalEvents = total
        };
    }

    /// <summary>
    /// The 0..1 normalised intensity for a given hour and tier, capped by <paramref name="cap"/>.
    /// Used by the renderer so a single very busy hour does not wash out the rest of the strip.
    /// </summary>
    public static double Normalise(int count, int cap) =>
        cap <= 0 ? 0d : Math.Min(1d, (double)count / cap);
}
