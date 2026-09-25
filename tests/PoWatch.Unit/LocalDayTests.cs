using PoWatch.Domain.Services;

namespace PoWatch.Unit;

public sealed class LocalDayTests
{
    private static readonly TimeZoneInfo UtcMinus5 =
        TimeZoneInfo.CreateCustomTimeZone("Test/UTC-5", TimeSpan.FromHours(-5), "UTC-5", "UTC-5");

    [Fact]
    public void A_late_evening_instant_belongs_to_the_local_day_not_the_utc_one()
    {
        // 23:28 local on the 24th is already 03:28 UTC on the 25th — the case that broke archive reads.
        var instant = new DateTimeOffset(2026, 9, 25, 3, 28, 0, TimeSpan.Zero);

        Assert.Equal(new DateOnly(2026, 9, 24), LocalDay.Of(instant, UtcMinus5));

        var (startUtc, endUtc) = LocalDay.Window(new DateOnly(2026, 9, 24), UtcMinus5);
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 5, 0, 0, TimeSpan.Zero), startUtc);
        Assert.Equal(new DateTimeOffset(2026, 9, 25, 5, 0, 0, TimeSpan.Zero), endUtc);
        Assert.InRange(instant, startUtc, endUtc);
    }

    [Fact]
    public void Daylight_saving_days_are_23_and_25_hours_long()
    {
        var newYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

        var (springStart, springEnd) = LocalDay.Window(new DateOnly(2026, 3, 8), newYork);
        var (fallStart, fallEnd) = LocalDay.Window(new DateOnly(2026, 11, 1), newYork);

        Assert.Equal(TimeSpan.FromHours(23), springEnd - springStart);
        Assert.Equal(TimeSpan.FromHours(25), fallEnd - fallStart);
    }
}
