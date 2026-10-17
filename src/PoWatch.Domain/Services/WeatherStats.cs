using PoWatch.Domain.Models;

namespace PoWatch.Domain.Services;

/// <summary>Occupancy on wet days against dry days, each the mean over that many observed days.</summary>
public sealed record WetDryComparison(int WetDays, int DryDays, double WetOccupancy, double DryOccupancy);

/// <summary>Plain words for WMO weather codes, and what the weather has to do with how busy the room is.</summary>
public static class WeatherStats
{
    /// <summary>A day counts as wet from a millimetre of rain or snow melt; less is a passing drizzle.</summary>
    public const double WetFromMm = 1.0;

    /// <summary>Each side needs this many observed days before a comparison means anything.</summary>
    public const int MinDaysPerSide = 3;

    /// <summary>The WMO 4677 code groups Open-Meteo reports, in words.</summary>
    public static string Describe(int code) => code switch
    {
        0 => "Clear",
        1 => "Mostly clear",
        2 => "Partly cloudy",
        3 => "Overcast",
        45 or 48 => "Fog",
        51 or 53 or 55 => "Drizzle",
        56 or 57 => "Freezing drizzle",
        61 => "Light rain",
        63 => "Rain",
        65 => "Heavy rain",
        66 or 67 => "Freezing rain",
        71 or 73 or 75 or 77 => "Snow",
        80 or 81 or 82 => "Showers",
        85 or 86 => "Snow showers",
        95 => "Thunderstorm",
        96 or 99 => "Thunderstorm with hail",
        _ => "Unknown"
    };

    public static bool IsWet(DailyWeather day) => day.PrecipitationMm >= WetFromMm;

    /// <summary>
    /// Mean occupancy of observed days split by wet and dry, or null until both sides have
    /// <see cref="MinDaysPerSide"/> days. Days without weather are left out.
    /// </summary>
    public static WetDryComparison? CompareWetDry(IEnumerable<(DateOnly Date, double Occupancy)> observedDays, IReadOnlyDictionary<DateOnly, DailyWeather> weather)
    {
        ArgumentNullException.ThrowIfNull(observedDays);
        ArgumentNullException.ThrowIfNull(weather);

        var (wet, dry) = (new List<double>(), new List<double>());
        foreach (var (date, occupancy) in observedDays)
        {
            if (!weather.TryGetValue(date, out var day)) continue;
            (IsWet(day) ? wet : dry).Add(occupancy);
        }

        return wet.Count < MinDaysPerSide || dry.Count < MinDaysPerSide
            ? null
            : new WetDryComparison(wet.Count, dry.Count, wet.Average(), dry.Average());
    }
}
