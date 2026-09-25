namespace PoWatch.Domain.Services;

/// <summary>
/// Maps between instants and the user's local calendar day. Storage partitions by UTC date, but
/// people think in local days, so every "today" or "the 24th" has to go through here — otherwise
/// the evening hours land in tomorrow's UTC partition and vanish from tonight's stats.
/// </summary>
public static class LocalDay
{
    /// <summary>The local calendar day that <paramref name="instant"/> falls on in <paramref name="zone"/>.</summary>
    public static DateOnly Of(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);

    /// <summary>The local calendar day currently in progress.</summary>
    public static DateOnly Today(TimeProvider time, TimeZoneInfo zone) => Of(time.GetUtcNow(), zone);

    /// <summary>
    /// The half-open UTC window [start, end) covering local midnight to local midnight. Days that
    /// cross a daylight-saving change come out 23 or 25 hours long.
    /// </summary>
    public static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) Window(DateOnly localDate, TimeZoneInfo zone)
    {
        var midnight = localDate.ToDateTime(TimeOnly.MinValue);
        return (ToUtc(midnight, zone), ToUtc(midnight.AddDays(1), zone));
    }

    /// <summary>
    /// Interprets a local wall-clock time as an instant. Inside a spring-forward gap the offset on
    /// either side of the gap is used, so windows stay contiguous instead of throwing.
    /// </summary>
    public static DateTimeOffset ToUtc(DateTime localWallClock, TimeZoneInfo zone) =>
        new DateTimeOffset(DateTime.SpecifyKind(localWallClock, DateTimeKind.Unspecified), zone.GetUtcOffset(localWallClock))
            .ToUniversalTime();
}
