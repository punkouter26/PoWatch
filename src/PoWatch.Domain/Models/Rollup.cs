using System.Numerics.Tensors;

namespace PoWatch.Domain.Models;

public enum RollupGrain
{
    Minute,
    Hour,
    Day,
    AllTime
}

/// <summary>Count, sum, sum of squares, min and max: enough to merge means and standard deviations.</summary>
public sealed record RunningStat(long Count, double Sum, double SumSquares, double Min, double Max)
{
    public static readonly RunningStat Empty = new(0, 0, 0, double.PositiveInfinity, double.NegativeInfinity);

    public static RunningStat Of(double value) => new(1, value, value * value, value, value);

    public double Mean => Count == 0 ? 0 : Sum / Count;

    public double StdDev => Count < 2 ? 0 : Math.Sqrt(Math.Max(0, (SumSquares - (Sum * Sum / Count)) / (Count - 1)));

    public RunningStat Merge(RunningStat other) => new(
        Count + other.Count,
        Sum + other.Sum,
        SumSquares + other.SumSquares,
        Math.Min(Min, other.Min),
        Math.Max(Max, other.Max));
}

/// <summary>How often a class appeared in a bucket, its peak simultaneous count, and the summed per-tick means.</summary>
public sealed record ClassTotals(long TicksPresent, int PeakCount, double MeanSum)
{
    public ClassTotals Merge(ClassTotals other) =>
        new(TicksPresent + other.TicksPresent, Math.Max(PeakCount, other.PeakCount), MeanSum + other.MeanSum);
}

/// <summary>
/// A mergeable summary of any number of ticks. Merge is associative and commutative with
/// <see cref="Empty"/> as identity, so minute, hour, day and all-time buckets can be built in any
/// order and replayed batches can be folded in without re-reading raw ticks.
/// </summary>
public sealed record Rollup
{
    public static readonly Rollup Empty = new();

    /// <summary>Earliest tick start folded in; <see cref="DateTimeOffset.MaxValue"/> when empty.</summary>
    public DateTimeOffset BucketStartUtc { get; init; } = DateTimeOffset.MaxValue;
    public long Ticks { get; init; }
    public double Seconds { get; init; }
    public long PixelSamples { get; init; }
    public long DetectorSamples { get; init; }

    /// <summary>Ticks in which at least one person or animal was present.</summary>
    public long OccupiedTicks { get; init; }

    /// <summary>Most people and animals seen at once in any single tick.</summary>
    public int PeakConcurrency { get; init; }

    /// <summary>Per-tick mean motion.</summary>
    public RunningStat Motion { get; init; } = RunningStat.Empty;

    /// <summary>Per-tick peak motion.</summary>
    public RunningStat MotionPeak { get; init; } = RunningStat.Empty;

    public RunningStat Luminance { get; init; } = RunningStat.Empty;

    /// <summary>Summed motion per <see cref="SpatialGrid"/> cell; empty until a tick with a grid arrives.</summary>
    public ReadOnlyMemory<float> Grid { get; init; } = ReadOnlyMemory<float>.Empty;

    /// <summary>Summed presence sightings per <see cref="SpatialGrid"/> cell — where people and animals hang out.</summary>
    public ReadOnlyMemory<float> PresenceGrid { get; init; } = ReadOnlyMemory<float>.Empty;

    public IReadOnlyDictionary<string, ClassTotals> Classes { get; init; } = new Dictionary<string, ClassTotals>();

    /// <summary>People and animals that entered the frame.</summary>
    public long Visits { get; init; }

    /// <summary>Dwell times keyed by <see cref="DwellBin"/> — a quarter-octave log scale, so percentiles merge.</summary>
    public IReadOnlyDictionary<int, long> DwellHistogram { get; init; } = new Dictionary<int, long>();

    public double DwellMaxSeconds { get; init; }

    public IReadOnlyDictionary<FrameEdge, long> Entries { get; init; } = new Dictionary<FrameEdge, long>();

    public IReadOnlyDictionary<FrameEdge, long> Exits { get; init; } = new Dictionary<FrameEdge, long>();

    /// <summary>Colour histogram keyed by 12-bit 0xRGB bins.</summary>
    public IReadOnlyDictionary<int, long> Palette { get; init; } = new Dictionary<int, long>();

    public double OccupancyFraction => Ticks == 0 ? 0 : (double)OccupiedTicks / Ticks;

    public static Rollup FromTick(Tick tick)
    {
        ArgumentNullException.ThrowIfNull(tick);

        var presenceCount = tick.Classes
            .Where(kv => EntityClasses.IsPresence(kv.Key))
            .Sum(kv => kv.Value.Max);

        return new Rollup
        {
            BucketStartUtc = tick.StartUtc,
            Ticks = 1,
            Seconds = tick.DurationSeconds,
            PixelSamples = tick.PixelSamples,
            DetectorSamples = tick.DetectorSamples,
            OccupiedTicks = presenceCount > 0 ? 1 : 0,
            PeakConcurrency = presenceCount,
            Motion = RunningStat.Of(tick.MotionMean),
            MotionPeak = RunningStat.Of(tick.MotionMax),
            Luminance = RunningStat.Of(tick.LuminanceMean),
            Grid = tick.MotionGrid.Count == SpatialGrid.Cells ? tick.MotionGrid.ToArray() : ReadOnlyMemory<float>.Empty,
            PresenceGrid = tick.PresenceGrid.Count == SpatialGrid.Cells ? tick.PresenceGrid.ToArray() : ReadOnlyMemory<float>.Empty,
            Classes = tick.Classes
                .Where(kv => kv.Value.Max > 0)
                .ToDictionary(kv => kv.Key, kv => new ClassTotals(1, kv.Value.Max, kv.Value.Mean), StringComparer.Ordinal),
            Palette = tick.Palette
                .GroupBy(QuantizeColor)
                .ToDictionary(g => g.Key, g => (long)g.Count())
        };
    }

    /// <summary>
    /// Folds a scene event into a rollup: people and animals entering count as visits, exits carry
    /// their dwell time, and both record which edge of the frame they used. Other events carry no
    /// counters and fold in as <see cref="Empty"/> apart from their timestamp.
    /// </summary>
    public static Rollup FromEvent(SceneEvent sceneEvent)
    {
        ArgumentNullException.ThrowIfNull(sceneEvent);
        var rollup = new Rollup { BucketStartUtc = sceneEvent.AtUtc };

        if (sceneEvent.Class is null || !EntityClasses.IsPresence(sceneEvent.Class))
            return rollup;

        return sceneEvent.Kind switch
        {
            SceneEventKind.TrackEnter => rollup with
            {
                Visits = 1,
                Entries = new Dictionary<FrameEdge, long> { [sceneEvent.Edge] = 1 }
            },
            SceneEventKind.TrackExit => rollup with
            {
                Exits = new Dictionary<FrameEdge, long> { [sceneEvent.Edge] = 1 },
                DwellHistogram = sceneEvent.DwellSeconds is { } dwell
                    ? new Dictionary<int, long> { [DwellBin(dwell)] = 1 }
                    : new Dictionary<int, long>(),
                DwellMaxSeconds = sceneEvent.DwellSeconds ?? 0
            },
            _ => rollup
        };
    }

    /// <summary>Quarter-octave bin for a dwell time: four bins per doubling, about 19% wide.</summary>
    public static int DwellBin(double seconds) => (int)Math.Floor(4 * Math.Log2(Math.Max(1, seconds)));

    /// <summary>The geometric centre of a <see cref="DwellBin"/>, in seconds.</summary>
    public static double DwellBinCentre(int bin) => Math.Pow(2, (bin + 0.5) / 4);

    public static Rollup Merge(Rollup a, Rollup b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        return new Rollup
        {
            BucketStartUtc = a.BucketStartUtc < b.BucketStartUtc ? a.BucketStartUtc : b.BucketStartUtc,
            Ticks = a.Ticks + b.Ticks,
            Seconds = a.Seconds + b.Seconds,
            PixelSamples = a.PixelSamples + b.PixelSamples,
            DetectorSamples = a.DetectorSamples + b.DetectorSamples,
            OccupiedTicks = a.OccupiedTicks + b.OccupiedTicks,
            PeakConcurrency = Math.Max(a.PeakConcurrency, b.PeakConcurrency),
            Motion = a.Motion.Merge(b.Motion),
            MotionPeak = a.MotionPeak.Merge(b.MotionPeak),
            Luminance = a.Luminance.Merge(b.Luminance),
            Grid = MergeGrids(a.Grid, b.Grid),
            PresenceGrid = MergeGrids(a.PresenceGrid, b.PresenceGrid),
            Classes = MergeMaps(a.Classes, b.Classes, (x, y) => x.Merge(y)),
            Palette = MergeMaps(a.Palette, b.Palette, (x, y) => x + y),
            Visits = a.Visits + b.Visits,
            DwellHistogram = MergeMaps(a.DwellHistogram, b.DwellHistogram, (x, y) => x + y),
            DwellMaxSeconds = Math.Max(a.DwellMaxSeconds, b.DwellMaxSeconds),
            Entries = MergeMaps(a.Entries, b.Entries, (x, y) => x + y),
            Exits = MergeMaps(a.Exits, b.Exits, (x, y) => x + y)
        };
    }

    /// <summary>Folds an 0xRRGGBB colour into a 12-bit 0xRGB bin (top four bits per channel).</summary>
    public static int QuantizeColor(int rgb) =>
        (((rgb >> 20) & 0xF) << 8) | (((rgb >> 12) & 0xF) << 4) | ((rgb >> 4) & 0xF);

    private static ReadOnlyMemory<float> MergeGrids(ReadOnlyMemory<float> a, ReadOnlyMemory<float> b)
    {
        if (a.IsEmpty) return b;
        if (b.IsEmpty) return a;

        var sum = new float[SpatialGrid.Cells];
        TensorPrimitives.Add(a.Span, b.Span, sum);
        return sum;
    }

    private static Dictionary<TKey, TValue> MergeMaps<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> a,
        IReadOnlyDictionary<TKey, TValue> b,
        Func<TValue, TValue, TValue> merge)
        where TKey : notnull
    {
        var result = new Dictionary<TKey, TValue>(a);
        foreach (var (key, value) in b)
            result[key] = result.TryGetValue(key, out var existing) ? merge(existing, value) : value;
        return result;
    }
}

/// <summary>Where each grain's bucket starts for an instant, in the user's local wall-clock time.</summary>
public static class RollupBuckets
{
    public static DateTimeOffset StartUtc(RollupGrain grain, DateTimeOffset instant, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = TimeZoneInfo.ConvertTime(instant, zone);

        // Truncating the local DateTimeOffset keeps its own offset, so a repeated fall-back hour still
        // maps to the right instant.
        return grain switch
        {
            RollupGrain.Minute => new DateTimeOffset(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0, local.Offset).ToUniversalTime(),
            RollupGrain.Hour => new DateTimeOffset(local.Year, local.Month, local.Day, local.Hour, 0, 0, local.Offset).ToUniversalTime(),
            RollupGrain.Day => Services.LocalDay.Window(DateOnly.FromDateTime(local.DateTime), zone).StartUtc,
            _ => DateTimeOffset.UnixEpoch
        };
    }
}

/// <summary>Detector class groups that stats care about.</summary>
public static class EntityClasses
{
    private static readonly HashSet<string> Presence = new(StringComparer.OrdinalIgnoreCase)
    {
        "person", "cat", "dog", "bird", "horse", "sheep", "cow", "elephant", "bear", "zebra", "giraffe"
    };

    /// <summary>People and animals — the classes that make a scene "occupied".</summary>
    public static bool IsPresence(string className) => Presence.Contains(className);
}
