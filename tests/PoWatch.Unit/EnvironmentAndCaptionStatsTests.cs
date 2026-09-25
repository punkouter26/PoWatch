using PoWatch.Domain.Models;
using PoWatch.Domain.Services;

namespace PoWatch.Unit;

public sealed class EnvironmentAndCaptionStatsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 24, 10, 0, 0, TimeSpan.Zero);

    private static readonly double[] RoomLuminance = [0.05, 0.05, 0.06, 0.60, 0.61, 0.60, 0.08, 0.07];

    private static Rollup Minute(int minute, double luminance, int color = 0x000000) =>
        Rollup.FromTick(new Tick
        {
            SessionId = Guid.NewGuid(),
            StartUtc = T0.AddMinutes(minute),
            LuminanceMean = luminance,
            Palette = [color]
        });

    [Fact]
    public void Light_steps_read_as_switches_while_slow_ramps_read_as_daylight()
    {
        // A room: dark, lights on at minute 3, off at minute 6.
        var room = RoomLuminance.Select((lum, i) => Minute(i, lum)).ToList();

        var switches = EnvironmentStats.LightSwitches(room);
        Assert.Equal([(T0.AddMinutes(3), true), (T0.AddMinutes(6), false)], switches.Select(s => (s.AtUtc, s.On)));
        Assert.Null(EnvironmentStats.EstimateDaylight(room));

        // A window: brightness ramps up 0.05/minute from 0.1 to 0.8, holds, then ramps down.
        var ramp = Enumerable.Range(0, 15).Select(i => 0.1 + (0.05 * i))
            .Concat(Enumerable.Repeat(0.8, 5))
            .Concat(Enumerable.Range(1, 14).Select(i => 0.8 - (0.05 * i)))
            .Select((lum, i) => Minute(i, lum)).ToList();

        Assert.Empty(EnvironmentStats.LightSwitches(ramp));
        var daylight = EnvironmentStats.EstimateDaylight(ramp);
        Assert.NotNull(daylight);
        // Midpoint 0.45 is first reached at minute 7 (0.45) and last held at minute 26 (0.45).
        Assert.Equal(T0.AddMinutes(7), daylight!.SunriseUtc);
        Assert.Equal(T0.AddMinutes(26), daylight.SunsetUtc);

        var curve = EnvironmentStats.LightCurve(room);
        Assert.Equal(8, curve.Count);
        Assert.Equal(0.60, curve[3].Luminance);

        // Palette: three ticks of orange, one of near-black → orange leads with 75%.
        var total = new[] { Minute(0, 0.5, 0xFF8000), Minute(1, 0.5, 0xF08A0F), Minute(2, 0.5, 0xFA8404), Minute(3, 0.5, 0x101010) }
            .Aggregate(Rollup.Merge);
        var palette = EnvironmentStats.DominantColors(total, 2);
        Assert.Equal(0xF88808, palette[0].Rgb);
        Assert.Equal(0.75, palette[0].Share);
        Assert.Equal(2, palette.Count);
    }

    [Fact]
    public void Captions_yield_word_counts_and_a_weirdest_moment()
    {
        var at = T0;
        (DateTimeOffset, string) C(string text) => (at = at.AddMinutes(1), text);

        var captions = new List<(DateTimeOffset AtUtc, string Text)>
        {
            C("A person sits at the desk typing on a laptop."),
            C("The person is typing at the desk."),
            C("A person typing on a laptop at a desk."),
            C("A cat jumps onto the windowsill chasing a moth!"),
            C("The person sits at the desk with a laptop."),
        };

        var words = CaptionStats.WordFrequencies(captions.Select(c => c.Text), top: 3);
        // Ties break alphabetically.
        Assert.Equal([("desk", 4), ("person", 4), ("laptop", 3)], words.Select(w => (w.Word, w.Count)));

        var weirdest = CaptionStats.Weirdest(captions);
        Assert.NotNull(weirdest);
        Assert.Contains("cat", weirdest!.Value.Text, StringComparison.Ordinal);

        Assert.Null(CaptionStats.Weirdest(captions.Take(2).ToList()));
        Assert.Equal(1.0, CaptionStats.Jaccard("The cat", "cat the"));
        Assert.Equal(0.0, CaptionStats.Jaccard("dog", "cat"));
    }
}
