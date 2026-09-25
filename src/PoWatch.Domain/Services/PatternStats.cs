using PoWatch.Domain.Models;

namespace PoWatch.Domain.Services;

/// <summary>Stat family C — when things happen, in the user's local hours and weekdays.</summary>
public static class PatternStats
{
    /// <summary>
    /// Mean of <paramref name="metric"/> per local weekday (indexed by <see cref="DayOfWeek"/>) and
    /// hour. Averages over the hours actually recorded, so one busy Monday and one idle Monday make
    /// a middling Monday rather than a busy one.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<double>> HourByWeekday(
        IEnumerable<Rollup> hourly, TimeZoneInfo zone, Func<Rollup, double> metric)
    {
        var sums = new double[7, 24];
        var counts = new int[7, 24];

        foreach (var rollup in Recorded(hourly))
        {
            var local = TimeZoneInfo.ConvertTime(rollup.BucketStartUtc, zone);
            sums[(int)local.DayOfWeek, local.Hour] += metric(rollup);
            counts[(int)local.DayOfWeek, local.Hour]++;
        }

        return Enumerable.Range(0, 7)
            .Select(d => (IReadOnlyList<double>)Enumerable.Range(0, 24)
                .Select(h => counts[d, h] == 0 ? 0 : sums[d, h] / counts[d, h])
                .ToArray())
            .ToArray();
    }

    /// <summary>Mean of <paramref name="metric"/> per local hour of day (24 values).</summary>
    public static IReadOnlyList<double> HourlyProfile(IEnumerable<Rollup> hourly, TimeZoneInfo zone, Func<Rollup, double> metric)
    {
        var sums = new double[24];
        var counts = new int[24];

        foreach (var rollup in Recorded(hourly))
        {
            var hour = TimeZoneInfo.ConvertTime(rollup.BucketStartUtc, zone).Hour;
            sums[hour] += metric(rollup);
            counts[hour]++;
        }

        return Enumerable.Range(0, 24).Select(h => counts[h] == 0 ? 0 : sums[h] / counts[h]).ToArray();
    }

    /// <summary>
    /// The local hour still ahead today that has historically been busiest on this weekday, or null
    /// when history has nothing for the rest of the day.
    /// </summary>
    public static int? NextBusyHour(IEnumerable<Rollup> hourlyHistory, TimeZoneInfo zone, DateTimeOffset nowUtc, Func<Rollup, double> metric)
    {
        var now = TimeZoneInfo.ConvertTime(nowUtc, zone);
        var byHour = HourByWeekday(hourlyHistory, zone, metric)[(int)now.DayOfWeek];

        var best = Enumerable.Range(now.Hour, 24 - now.Hour)
            .Where(h => h > now.Hour || now.Minute == 0)
            .Where(h => byHour[h] > 0)
            .OrderByDescending(h => byHour[h])
            .ThenBy(h => h)
            .Select(h => (int?)h)
            .FirstOrDefault();

        return best;
    }

    private static IEnumerable<Rollup> Recorded(IEnumerable<Rollup> rollups) =>
        rollups.Where(r => r.Ticks > 0);
}
