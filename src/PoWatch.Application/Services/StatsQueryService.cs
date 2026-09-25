using PoWatch.Application.Contracts;
using PoWatch.Domain.Models;
using PoWatch.Domain.Services;
using PoWatch.Shared.Models;

namespace PoWatch.Application.Services;

public enum StatsRange
{
    Session,
    Today,
    Week,
    Month,
    All,
    Day
}

/// <summary>A resolved stats window: the instants it covers and the coarsest rollup grain that answers it.</summary>
public sealed record StatsWindow(StatsRange Range, DateTimeOffset FromUtc, DateTimeOffset ToUtc, RollupGrain Grain, TimeZoneInfo Zone)
{
    public TimeSpan GrainLength => Grain switch
    {
        RollupGrain.Minute => TimeSpan.FromMinutes(1),
        RollupGrain.Hour => TimeSpan.FromHours(1),
        _ => TimeSpan.FromDays(1)
    };

    public StatsWindowDto ToDto() => new()
    {
        Range = StatsQueryService.RangeName(Range),
        FromUtc = FromUtc,
        ToUtc = ToUtc,
        Grain = Grain.ToString(),
        TimeZoneId = Zone.Id
    };
}

/// <summary>
/// Reads rollups for a window and runs the domain stat calculators over them. Everything here is
/// composition; the maths lives in <c>PoWatch.Domain.Services</c> and is unit-tested there.
/// </summary>
public sealed class StatsQueryService(IRollupStore rollups, ISensingLog sensingLog, ISessionRepository sessions, TimeProvider time)
{
    /// <summary>Days that make up "your usual".</summary>
    public const int BaselineDays = 28;

    private const int CalendarDays = 365;

    private static readonly Dictionary<string, StatsRange> RangeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["session"] = StatsRange.Session,
        ["today"] = StatsRange.Today,
        ["7d"] = StatsRange.Week,
        ["30d"] = StatsRange.Month,
        ["all"] = StatsRange.All,
        ["day"] = StatsRange.Day
    };

    public static bool TryParseRange(string? value, out StatsRange range) =>
        RangeNames.TryGetValue(value ?? "today", out range);

    public static string RangeName(StatsRange range) => RangeNames.First(kv => kv.Value == range).Key;

    /// <summary>
    /// Null when the range is <see cref="StatsRange.Session"/> and the session is not the user's, or
    /// <see cref="StatsRange.Day"/> without a date.
    /// </summary>
    public async Task<StatsWindow?> ResolveAsync(string userId, StatsRange range, string? timeZoneId, Guid? sessionId, CancellationToken cancellationToken, DateOnly? date = null)
    {
        var session = sessionId is { } id ? await sessions.GetAsync(userId, id, cancellationToken) : null;
        if (range == StatsRange.Session && session is null) return null;
        if (range == StatsRange.Day && date is null) return null;

        var zone = FindZone(timeZoneId) ?? session?.TimeZone ?? TimeZoneInfo.Utc;
        var now = time.GetUtcNow();
        var today = LocalDay.Today(time, zone);
        DateTimeOffset DayStart(int daysAgo) => LocalDay.Window(today.AddDays(-daysAgo), zone).StartUtc;

        return range switch
        {
            StatsRange.Session => new StatsWindow(range,
                RollupBuckets.StartUtc(RollupGrain.Minute, session!.StartedUtc, zone),
                (session.EndedUtc ?? now).AddMinutes(1), RollupGrain.Minute, zone),
            StatsRange.Week => new StatsWindow(range, DayStart(6), now.AddHours(1), RollupGrain.Hour, zone),
            StatsRange.Month => new StatsWindow(range, DayStart(29), now.AddHours(1), RollupGrain.Hour, zone),
            StatsRange.All => new StatsWindow(range, DateTimeOffset.UnixEpoch, now.AddDays(1), RollupGrain.Day, zone),
            StatsRange.Day => LocalDay.Window(date!.Value, zone) is var (start, end)
                ? new StatsWindow(range, start, end, RollupGrain.Minute, zone)
                : null,
            _ => new StatsWindow(range, DayStart(0), now.AddMinutes(1), RollupGrain.Minute, zone)
        };
    }

    public async Task<PresenceStatsDto> PresenceAsync(string userId, StatsWindow window, CancellationToken cancellationToken)
    {
        var series = await SeriesAsync(userId, window, cancellationToken);
        var summary = PresenceStats.Summarize(series, window.GrainLength);
        var total = Total(series);

        return new PresenceStatsDto
        {
            Window = window.ToDto(),
            SecondsObserved = total.Seconds,
            Occupancy = summary.Occupancy,
            Visits = total.Visits,
            VisitsPerHour = summary.VisitsPerHour,
            PeakConcurrency = summary.PeakConcurrency,
            DwellP50Seconds = summary.DwellP50Seconds,
            DwellP90Seconds = summary.DwellP90Seconds,
            DwellMaxSeconds = summary.DwellMaxSeconds,
            LongestEmptySeconds = summary.LongestEmptyStreak.TotalSeconds,
            LongestStillSeconds = summary.LongestStillStreak.TotalSeconds,
            BusiestStartUtc = summary.BusiestStartUtc,
            BusiestMotion = summary.BusiestMotion,
            Series = series.Select(r => new SeriesPointDto
            {
                AtUtc = r.BucketStartUtc,
                Motion = r.Motion.Mean,
                MotionPeak = r.MotionPeak.Count == 0 ? 0 : r.MotionPeak.Max,
                Occupancy = r.OccupancyFraction,
                Luminance = r.Luminance.Mean,
                Visits = r.Visits,
                PeakConcurrency = r.PeakConcurrency
            }).ToList()
        };
    }

    public async Task<SpaceStatsDto> SpaceAsync(string userId, StatsWindow window, CancellationToken cancellationToken)
    {
        var space = SpaceStats.Summarize(Total(await SeriesAsync(userId, window, cancellationToken)));
        return new SpaceStatsDto
        {
            Window = window.ToDto(),
            MotionHeat = space.MotionHeat.ToList(),
            PresenceHeat = space.PresenceHeat.ToList(),
            HottestCell = space.HottestCell,
            FavouriteSpotCell = space.FavouriteSpotCell,
            Entries = space.Entries.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
            Exits = space.Exits.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
            BusiestEdge = space.BusiestEdge.ToString()
        };
    }

    public async Task<ObjectStatsDto> ObjectsAsync(string userId, StatsWindow window, CancellationToken cancellationToken)
    {
        var total = Total(await SeriesAsync(userId, window, cancellationToken));
        var classes = total.Classes
            .Select(kv => new ClassStatDto
            {
                Name = kv.Key,
                TicksPresent = kv.Value.TicksPresent,
                Peak = kv.Value.PeakCount,
                MeanCount = kv.Value.TicksPresent == 0 ? 0 : kv.Value.MeanSum / kv.Value.TicksPresent,
                Share = total.Ticks == 0 ? 0 : (double)kv.Value.TicksPresent / total.Ticks
            })
            .OrderByDescending(c => c.TicksPresent)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .ToList();

        return new ObjectStatsDto
        {
            Window = window.ToDto(),
            Classes = classes,
            Rarest = classes.Count > 1 ? classes[^1].Name : null
        };
    }

    public async Task<PatternStatsDto> PatternsAsync(string userId, StatsWindow window, CancellationToken cancellationToken)
    {
        var zone = window.Zone;
        var now = time.GetUtcNow();
        var today = LocalDay.Today(time, zone);
        var todayStart = LocalDay.Window(today, zone).StartUtc;
        var baselineStart = LocalDay.Window(today.AddDays(-BaselineDays), zone).StartUtc;

        var baselineHours = await rollups.GetRangeAsync(userId, RollupGrain.Hour, baselineStart, todayStart, cancellationToken);
        var todayHours = await rollups.GetRangeAsync(userId, RollupGrain.Hour, todayStart, now.AddHours(1), cancellationToken);
        var calendar = await rollups.GetRangeAsync(userId, RollupGrain.Day,
            LocalDay.Window(today.AddDays(-(CalendarDays - 1)), zone).StartUtc, now.AddDays(1), cancellationToken);

        static double Occupancy(Rollup r) => r.OccupancyFraction;
        static double VisitsPerHour(Rollup r) => r.Seconds > 0 ? r.Visits / (r.Seconds / 3600) : 0;

        var baselineDays = calendar.Where(r => r.Ticks > 0 && r.BucketStartUtc >= baselineStart && r.BucketStartUtc < todayStart).ToList();
        var todayTotal = Total(todayHours);
        var usualProfile = PatternStats.HourlyProfile(baselineHours, zone, Occupancy);
        var todayProfile = PatternStats.HourlyProfile(todayHours, zone, Occupancy);

        return new PatternStatsDto
        {
            Window = window.ToDto(),
            HourByWeekday = PatternStats.HourByWeekday(baselineHours, zone, Occupancy).Select(d => d.ToList()).ToList(),
            TodayProfile = todayProfile.ToList(),
            UsualProfile = usualProfile.ToList(),
            RhythmScore = todayTotal.Ticks > 0 ? AnomalyMath.RhythmScore(todayProfile, usualProfile) : null,
            Anomalies = todayTotal.Ticks == 0
                ? []
                :
                [
                    Anomaly(AnomalyMath.Compare("occupancy", Occupancy(todayTotal), baselineDays.Select(Occupancy).ToList())),
                    Anomaly(AnomalyMath.Compare("visits/hour", VisitsPerHour(todayTotal), baselineDays.Select(VisitsPerHour).ToList())),
                    Anomaly(AnomalyMath.Compare("motion", todayTotal.Motion.Mean, baselineDays.Select(r => r.Motion.Mean).ToList())),
                ],
            OccupancyTrendPerDay = AnomalyMath.TrendSlope(baselineDays.Select(Occupancy).ToList()),
            NextBusyHour = PatternStats.NextBusyHour(baselineHours, zone, now, Occupancy),
            Calendar = calendar.Where(r => r.Ticks > 0).Select(r => new DayValueDto
            {
                Date = LocalDay.Of(r.BucketStartUtc, zone),
                Occupancy = r.OccupancyFraction,
                Visits = r.Visits,
                Motion = r.Motion.Mean
            }).ToList()
        };
    }

    public async Task<EnvironmentStatsDto> EnvironmentAsync(string userId, StatsWindow window, CancellationToken cancellationToken)
    {
        var series = await SeriesAsync(userId, window, cancellationToken);
        var shortWindow = window.Range is StatsRange.Today or StatsRange.Session or StatsRange.Day;
        var daylight = shortWindow ? EnvironmentStats.EstimateDaylight(series) : null;

        // Captions are raw events, so only the days a short window touches are read.
        var captions = new List<(DateTimeOffset AtUtc, string Text)>();
        if (shortWindow)
        {
            var firstDay = LocalDay.Of(window.FromUtc, window.Zone);
            var lastDay = LocalDay.Of(Min(window.ToUtc, time.GetUtcNow()), window.Zone);
            for (var day = firstDay; day <= lastDay; day = day.AddDays(1))
            {
                captions.AddRange((await sensingLog.GetEventsAsync(userId, day, cancellationToken))
                    .Where(e => e.Kind == SceneEventKind.Caption && e.Text is not null && e.AtUtc >= window.FromUtc && e.AtUtc < window.ToUtc)
                    .Select(e => (e.AtUtc, e.Text!)));
            }
        }

        var weirdest = CaptionStats.Weirdest(captions);
        return new EnvironmentStatsDto
        {
            Window = window.ToDto(),
            LightCurve = EnvironmentStats.LightCurve(series).Select(p => new LightPointDto { AtUtc = p.StartUtc, Luminance = p.Luminance }).ToList(),
            Switches = EnvironmentStats.LightSwitches(series).Select(s => new LightSwitchDto { AtUtc = s.AtUtc, On = s.On }).ToList(),
            SunriseUtc = daylight?.SunriseUtc,
            SunsetUtc = daylight?.SunsetUtc,
            Palette = EnvironmentStats.DominantColors(Total(series), 5).Select(c => new PaletteColorDto { Rgb = c.Rgb, Share = c.Share }).ToList(),
            CaptionCount = captions.Count,
            Words = CaptionStats.WordFrequencies(captions.Select(c => c.Text), 30).Select(w => new WordCountDto { Word = w.Word, Count = w.Count }).ToList(),
            WeirdestCaption = weirdest?.Text,
            WeirdestAtUtc = weirdest?.AtUtc
        };
    }

    public async Task<PipelineStatsDto> PipelineAsync(string userId, StatsWindow window, CancellationToken cancellationToken)
    {
        var total = Total(await SeriesAsync(userId, window, cancellationToken));
        var allTime = await rollups.GetAllTimeAsync(userId, cancellationToken);
        var recent = await sessions.ListAsync(userId, SessionService.MaxListed, cancellationToken);
        var running = recent.FirstOrDefault(s => s.IsRunning);

        return new PipelineStatsDto
        {
            Window = window.ToDto(),
            Ticks = total.Ticks,
            SecondsObserved = total.Seconds,
            PixelSamples = total.PixelSamples,
            DetectorSamples = total.DetectorSamples,
            PixelHz = total.Seconds > 0 ? total.PixelSamples / total.Seconds : 0,
            DetectorHz = total.Seconds > 0 ? total.DetectorSamples / total.Seconds : 0,
            AllTimeTicks = allTime.Ticks,
            AllTimeFrames = allTime.PixelSamples,
            AllTimeHours = allTime.Seconds / 3600,
            Sessions = recent.Count,
            RunningSessionSeconds = running?.Duration(time.GetUtcNow()).TotalSeconds
        };
    }

    private Task<IReadOnlyList<Rollup>> SeriesAsync(string userId, StatsWindow window, CancellationToken cancellationToken) =>
        rollups.GetRangeAsync(userId, window.Grain, window.FromUtc, window.ToUtc, cancellationToken);

    private static Rollup Total(IEnumerable<Rollup> series) => series.Aggregate(Rollup.Empty, Rollup.Merge);

    private static AnomalyDto Anomaly(Anomaly a) => new()
    {
        Metric = a.Metric,
        Today = a.Today,
        Mean = a.Mean,
        StdDev = a.StdDev,
        Z = a.Z,
        Flagged = a.Flagged
    };

    private static TimeZoneInfo? FindZone(string? id) =>
        !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? zone : null;

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
}
