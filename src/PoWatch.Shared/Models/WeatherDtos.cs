namespace PoWatch.Shared.Models;

public sealed class WeatherDayDto
{
    public DateOnly Date { get; init; }

    /// <summary>WMO weather code, and the same in words.</summary>
    public int Code { get; init; }
    public string Summary { get; init; } = string.Empty;
    public double TempMaxC { get; init; }
    public double TempMinC { get; init; }
    public double PrecipitationMm { get; init; }

    /// <summary>Rainy enough to count as a wet day in the wet-against-dry comparison (the server's rule, so both agree).</summary>
    public bool Wet { get; init; }
    public DateTimeOffset? SunriseUtc { get; init; }
    public DateTimeOffset? SunsetUtc { get; init; }
}

/// <summary>How busy wet days are against dry ones, once there are enough of each.</summary>
public sealed class WeatherInsightDto
{
    public int WetDays { get; init; }
    public int DryDays { get; init; }
    public double WetOccupancy { get; init; }
    public double DryOccupancy { get; init; }
    public string Sentence { get; init; } = string.Empty;
}

/// <summary>A year of daily weather at the caller's place, oldest first; empty until a place is chosen.</summary>
public sealed class WeatherYearDto
{
    public PlaceDto? Place { get; init; }
    public List<WeatherDayDto> Days { get; init; } = [];
    public WeatherInsightDto? Insight { get; init; }
}
