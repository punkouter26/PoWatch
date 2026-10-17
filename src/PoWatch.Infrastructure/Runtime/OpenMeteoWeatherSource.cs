using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using PoWatch.Application.Contracts;
using PoWatch.Domain.Models;
using PoWatch.Domain.Services;

namespace PoWatch.Infrastructure.Runtime;

/// <summary>
/// Daily weather and place search from Open-Meteo (free, no key). Older days come from the
/// reanalysis archive, which lags a few days; the last week comes from the forecast API. Past days
/// never change, so they are cached for a day; the recent week for an hour. Weather is context
/// only: any failure leaves the days out instead of failing the page.
/// </summary>
public sealed class OpenMeteoWeatherSource(HttpClient http, HybridCache cache, TimeProvider time, ILogger<OpenMeteoWeatherSource> logger) : IWeatherSource
{
    public const string ArchiveUrl = "https://archive-api.open-meteo.com/v1/archive";
    public const string ForecastUrl = "https://api.open-meteo.com/v1/forecast";
    public const string SearchUrl = "https://geocoding-api.open-meteo.com/v1/search";

    /// <summary>The archive is reliably complete up to this many days ago; newer days come from the forecast API.</summary>
    private const int RecentDays = 7;

    private const string Daily = "weather_code,temperature_2m_max,temperature_2m_min,precipitation_sum,sunrise,sunset";

    public async Task<IReadOnlyList<DailyWeather>> GetDailyAsync(Place place, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(place);
        var zone = LocalDay.FindZone(place.TimeZoneId) ?? TimeZoneInfo.Utc;
        var recentFrom = LocalDay.Today(time, zone).AddDays(-(RecentDays - 1));

        var days = new List<DailyWeather>();
        if (from < recentFrom)
            days.AddRange(await ReadAsync(ArchiveUrl, place, from, Min(to, recentFrom.AddDays(-1)), TimeSpan.FromDays(1), cancellationToken));
        if (to >= recentFrom)
            days.AddRange(await ReadAsync(ForecastUrl, place, Max(from, recentFrom), to, TimeSpan.FromHours(1), cancellationToken));
        return days;
    }

    public async Task<IReadOnlyList<(Place Place, string? Detail)>> SearchAsync(string name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < 2) return [];
        var url = $"{SearchUrl}?name={Uri.EscapeDataString(name.Trim())}&count=6&language=en&format=json";
        try
        {
            using var document = JsonDocument.Parse(await http.GetStringAsync(url, cancellationToken));
            if (!document.RootElement.TryGetProperty("results", out var results)) return [];
            return results.EnumerateArray().Select(r =>
            {
                var detail = string.Join(", ", new[] { Text(r, "admin1"), Text(r, "country") }.Where(s => !string.IsNullOrEmpty(s)));
                return (new Place(Text(r, "name") ?? "?", r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble(), Text(r, "timezone")),
                    string.IsNullOrEmpty(detail) ? null : detail);
            }).ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            logger.LogWarning(ex, "Place search failed.");
            return [];
        }
    }

    private async Task<IReadOnlyList<DailyWeather>> ReadAsync(string api, Place place, DateOnly from, DateOnly to, TimeSpan keep, CancellationToken cancellationToken)
    {
        if (to < from) return [];
        string Day(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var url = string.Create(CultureInfo.InvariantCulture,
            $"{api}?latitude={place.Latitude:0.###}&longitude={place.Longitude:0.###}&start_date={Day(from)}&end_date={Day(to)}&daily={Daily}&timezone=auto");
        try
        {
            // A failed read throws out of the factory, so nothing is cached and the next visit tries again.
            return await cache.GetOrCreateAsync($"weather:{url}", async ct => Parse(await http.GetStringAsync(url, ct)),
                new HybridCacheEntryOptions { Expiration = keep, LocalCacheExpiration = keep }, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Weather read failed for {From}..{To}; those days are left out.", from, to);
            return [];
        }
    }

    /// <summary>Open-Meteo's daily arrays, one entry per day; days the source has no code for yet are skipped.</summary>
    public static List<DailyWeather> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var zone = LocalDay.FindZone(Text(root, "timezone")) ?? TimeZoneInfo.Utc;
        var daily = root.GetProperty("daily");
        var dates = daily.GetProperty("time");

        var days = new List<DailyWeather>();
        for (var i = 0; i < dates.GetArrayLength(); i++)
        {
            if (Number(daily, "weather_code", i) is not { } code) continue;
            days.Add(new DailyWeather(
                DateOnly.ParseExact(dates[i].GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                (int)code,
                Number(daily, "temperature_2m_max", i) ?? 0,
                Number(daily, "temperature_2m_min", i) ?? 0,
                Number(daily, "precipitation_sum", i) ?? 0,
                Instant(daily, "sunrise", i, zone),
                Instant(daily, "sunset", i, zone)));
        }
        return days;
    }

    private static double? Number(JsonElement daily, string name, int i) =>
        daily.TryGetProperty(name, out var values) && values[i].ValueKind == JsonValueKind.Number ? values[i].GetDouble() : null;

    /// <summary>"2026-10-16T06:58" is the place's wall clock; it becomes an instant through the place's zone.</summary>
    private static DateTimeOffset? Instant(JsonElement daily, string name, int i, TimeZoneInfo zone) =>
        daily.TryGetProperty(name, out var values) && values[i].GetString() is { } local
            && DateTime.TryParseExact(local, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var wallClock)
            ? LocalDay.ToUtc(wallClock, zone)
            : null;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;
}
