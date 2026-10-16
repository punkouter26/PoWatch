namespace PoWatch.Domain.Models;

/// <summary>One local day's weather at a place: a WMO weather code, temperatures in °C, rain in mm, and the sun.</summary>
public sealed record DailyWeather(
    DateOnly Date,
    int Code,
    double TempMaxC,
    double TempMinC,
    double PrecipitationMm,
    DateTimeOffset? SunriseUtc,
    DateTimeOffset? SunsetUtc);
