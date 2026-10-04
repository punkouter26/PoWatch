using System.Globalization;

namespace PoWatch.Api.Features;

/// <summary>How every endpoint reads a local day and a time zone from the query string.</summary>
internal static class ApiParsing
{
    public const string DayFormat = "yyyy-MM-dd";

    public static bool TryDay(string? value, out DateOnly day) =>
        DateOnly.TryParseExact(value, DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out day);

    /// <summary>The named zone, or UTC when it is missing or unknown.</summary>
    public static TimeZoneInfo Zone(string? id) =>
        !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? zone : TimeZoneInfo.Utc;
}
