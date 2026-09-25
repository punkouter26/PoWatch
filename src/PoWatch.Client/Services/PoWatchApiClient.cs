using System.Net;
using System.Net.Http.Json;
using PoWatch.Shared.Models;

namespace PoWatch.Client.Services;

public sealed class PoWatchApiClient(HttpClient httpClient)
{
    private static readonly PoWatchJsonContext Json = PoWatchJsonContext.Default;

    public async Task<SessionDto?> StartSessionAsync(StartSessionRequestDto request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("api/sessions", request, Json.StartSessionRequestDto, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(Json.SessionDto, cancellationToken);
    }

    public async Task<SessionDto?> StopSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsync($"api/sessions/{sessionId}/stop", content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(Json.SessionDto, cancellationToken);
    }

    /// <summary>Empty on failure: Stats and History treat the list as optional, not as a reason to show the error page.</summary>
    public async Task<IReadOnlyList<SessionDto>> ListSessionsAsync(int take = 20, CancellationToken cancellationToken = default)
    {
        try
        {
            return await httpClient.GetFromJsonAsync($"api/sessions?take={take}", Json.ListSessionDto, cancellationToken) ?? [];
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }

    /// <summary>
    /// Posts one ingest batch. Returns null when the server refused it outright (4xx) so the caller
    /// can drop it; throws on transport or server errors, an expired sign-in, a timeout or rate
    /// limiting, so the caller keeps it for a retry.
    /// </summary>
    public async Task<IngestBatchResultDto?> PostBatchAsync(Guid sessionId, IngestBatchDto batch, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync($"api/sessions/{sessionId}/batches", batch, Json.IngestBatchDto, cancellationToken);
        var retryable = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests;
        if (!retryable && (int)response.StatusCode is >= 400 and < 500) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(Json.IngestBatchResultDto, cancellationToken);
    }

    /// <summary>A write link for one highlight snapshot; null when there is no image storage or today's cap is reached.</summary>
    public async Task<SnapshotUploadDto?> CreateSnapshotUploadAsync(DateOnly localDay, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsync($"api/snapshots?day={localDay:yyyy-MM-dd}", content: null, cancellationToken);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync(Json.SnapshotUploadDto, cancellationToken) : null;
    }

    public async Task<RecapDto?> GetDayRecapAsync(DateOnly day, string timeZoneId, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(DayRecapPath(day, timeZoneId, pdf: false), cancellationToken);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync(Json.RecapDto, cancellationToken) : null;
    }

    /// <summary>Relative link to a day's recap PDF; the BFF cookie rides along on a plain download.</summary>
    public static string DayRecapPdfUrl(DateOnly day, string timeZoneId) => DayRecapPath(day, timeZoneId, pdf: true);

    public static string SessionRecapPdfUrl(Guid sessionId) => $"api/recaps/session/{sessionId}.pdf";

    private static string DayRecapPath(DateOnly day, string timeZoneId, bool pdf) =>
        $"api/recaps/day/{day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}{(pdf ? ".pdf" : string.Empty)}?tz={Uri.EscapeDataString(timeZoneId)}";

    public async Task<IReadOnlyList<MomentDto>> GetMomentsAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"api/sessions/{sessionId}/moments", cancellationToken);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync(Json.ListMomentDto, cancellationToken) ?? [] : [];
    }

    public async Task<TrophyCabinetDto?> GetTrophiesAsync(CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync("api/achievements", Json.TrophyCabinetDto, cancellationToken);

    public async Task<IReadOnlyList<RegularDto>> ListRegularsAsync(CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync("api/regulars", Json.ListRegularDto, cancellationToken) ?? [];

    public async Task<ObserveRegularResultDto?> ObserveRegularAsync(ObserveRegularRequestDto request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("api/regulars/observe", request, Json.ObserveRegularRequestDto, cancellationToken);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync(Json.ObserveRegularResultDto, cancellationToken) : null;
    }

    public async Task<RegularDto?> RenameRegularAsync(string regularId, string? name, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Patch, $"api/regulars/{Uri.EscapeDataString(regularId)}")
        {
            Content = JsonContent.Create(new RenameRegularRequestDto { Name = name }, Json.RenameRegularRequestDto)
        };
        using var response = await httpClient.SendAsync(message, cancellationToken);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync(Json.RegularDto, cancellationToken) : null;
    }

    public async Task<RegularDto?> MergeRegularsAsync(string primaryId, string duplicateId, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("api/regulars/merge",
            new MergeRegularsRequestDto { PrimaryId = primaryId, DuplicateId = duplicateId }, Json.MergeRegularsRequestDto, cancellationToken);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync(Json.RegularDto, cancellationToken) : null;
    }

    public Task<PresenceStatsDto?> GetPresenceAsync(StatsQuery query, CancellationToken cancellationToken = default) =>
        GetStatsAsync("presence", query, Json.PresenceStatsDto, cancellationToken);

    public Task<SpaceStatsDto?> GetSpaceAsync(StatsQuery query, CancellationToken cancellationToken = default) =>
        GetStatsAsync("space", query, Json.SpaceStatsDto, cancellationToken);

    public Task<ObjectStatsDto?> GetObjectsAsync(StatsQuery query, CancellationToken cancellationToken = default) =>
        GetStatsAsync("objects", query, Json.ObjectStatsDto, cancellationToken);

    public Task<PatternStatsDto?> GetPatternsAsync(StatsQuery query, CancellationToken cancellationToken = default) =>
        GetStatsAsync("patterns", query, Json.PatternStatsDto, cancellationToken);

    public Task<EnvironmentStatsDto?> GetEnvironmentAsync(StatsQuery query, CancellationToken cancellationToken = default) =>
        GetStatsAsync("environment", query, Json.EnvironmentStatsDto, cancellationToken);

    public Task<PipelineStatsDto?> GetPipelineAsync(StatsQuery query, CancellationToken cancellationToken = default) =>
        GetStatsAsync("pipeline", query, Json.PipelineStatsDto, cancellationToken);

    /// <summary>Stats are nice-to-have on every page: a failed read returns null rather than throwing.</summary>
    private async Task<T?> GetStatsAsync<T>(string family, StatsQuery query, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type, CancellationToken cancellationToken)
    {
        var url = $"api/stats/{family}?range={Uri.EscapeDataString(query.Range)}&tz={Uri.EscapeDataString(query.TimeZoneId)}"
            + (query.SessionId is { } id ? $"&sessionId={id}" : string.Empty)
            + (query.Date is { } date ? $"&date={date:yyyy-MM-dd}" : string.Empty);
        try
        {
            using var response = await httpClient.GetAsync(url, cancellationToken);
            return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync(type, cancellationToken) : default;
        }
        catch (HttpRequestException)
        {
            return default;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient timeout, not the caller cancelling: without this the pages' refresh loops end for good.
            return default;
        }
    }

    public async Task<DiagnosticsSnapshotDto?> GetDiagnosticsAsync(CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync("api/diagnostics/status", Json.DiagnosticsSnapshotDto, cancellationToken);

    /// <summary>
    /// Reads the same <c>/health</c> document the App Service probe and the CI deploy gate read.
    /// HttpClient does not send an <c>Accept: text/html</c> header, so this always resolves to the
    /// JSON endpoint rather than the Health page's own HTML shell.
    /// </summary>
    public async Task<HealthReportDto?> GetHealthAsync(CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync("health", Json.HealthReportDto, cancellationToken);
}
