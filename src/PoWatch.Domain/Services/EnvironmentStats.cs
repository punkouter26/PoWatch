using PoWatch.Domain.Models;

namespace PoWatch.Domain.Services;

public sealed record LightPoint(DateTimeOffset StartUtc, double Luminance);

public sealed record LightSwitch(DateTimeOffset AtUtc, bool On);

public sealed record Daylight(DateTimeOffset SunriseUtc, DateTimeOffset SunsetUtc);

public sealed record PaletteColor(int Rgb, double Share);

/// <summary>Stat family D — light and colour.</summary>
public static class EnvironmentStats
{
    /// <summary>A jump in mean brightness between neighbouring buckets at least this big is a switch, not the sun.</summary>
    public const double SwitchStep = 0.2;

    /// <summary>A day whose brightness varies less than this is not daylight-shaped.</summary>
    public const double MinDaylightRange = 0.2;

    private const double Tolerance = 1e-9;

    public static IReadOnlyList<LightPoint> LightCurve(IEnumerable<Rollup> series) =>
        Recorded(series).Select(r => new LightPoint(r.BucketStartUtc, r.Luminance.Mean)).ToList();

    /// <summary>Sudden brightness steps between neighbouring buckets: someone flipped a switch.</summary>
    public static IReadOnlyList<LightSwitch> LightSwitches(IEnumerable<Rollup> series)
    {
        var curve = LightCurve(series);
        var switches = new List<LightSwitch>();

        for (var i = 1; i < curve.Count; i++)
        {
            var step = curve[i].Luminance - curve[i - 1].Luminance;
            if (Math.Abs(step) >= SwitchStep - Tolerance)
                switches.Add(new LightSwitch(curve[i].StartUtc, step > 0));
        }

        return switches;
    }

    /// <summary>
    /// Sunrise and sunset from a day's brightness curve: when it first reaches and last holds the
    /// midpoint between its darkest and brightest. Only a curve that ramps (no switch steps) with
    /// enough range counts as daylight; otherwise null.
    /// </summary>
    public static Daylight? EstimateDaylight(IEnumerable<Rollup> series)
    {
        var curve = LightCurve(series);
        if (curve.Count < 3 || LightSwitches(series).Count > 0) return null;

        var min = curve.Min(p => p.Luminance);
        var max = curve.Max(p => p.Luminance);
        if (max - min < MinDaylightRange) return null;

        var mid = min + ((max - min) / 2);
        var bright = curve.Where(p => p.Luminance >= mid - Tolerance).ToList();
        return new Daylight(bright[0].StartUtc, bright[^1].StartUtc);
    }

    /// <summary>The most frequent colour bins, each as its bin's centre colour and share of all samples.</summary>
    public static IReadOnlyList<PaletteColor> DominantColors(Rollup total, int count)
    {
        ArgumentNullException.ThrowIfNull(total);
        var samples = total.Palette.Values.Sum();
        if (samples == 0) return [];

        return total.Palette
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key)
            .Take(count)
            .Select(kv => new PaletteColor(BinCentre(kv.Key), (double)kv.Value / samples))
            .ToList();
    }

    /// <summary>Expands a 12-bit 0xRGB bin to the 0xRRGGBB colour at its centre.</summary>
    public static int BinCentre(int bin)
    {
        static int Channel(int nibble) => (nibble << 4) | 0x8;
        return (Channel((bin >> 8) & 0xF) << 16) | (Channel((bin >> 4) & 0xF) << 8) | Channel(bin & 0xF);
    }

    private static IEnumerable<Rollup> Recorded(IEnumerable<Rollup> series) =>
        series.Where(r => r.Ticks > 0).OrderBy(r => r.BucketStartUtc);
}
