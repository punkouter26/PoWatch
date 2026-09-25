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

    public async Task<IReadOnlyList<SessionDto>> ListSessionsAsync(int take = 20, CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync($"api/sessions?take={take}", Json.ListSessionDto, cancellationToken) ?? [];

    /// <summary>
    /// Posts one ingest batch. Returns null when the server refused it outright (4xx) so the caller
    /// can drop it; throws on transport or server errors so the caller keeps it for a retry.
    /// </summary>
    public async Task<IngestBatchResultDto?> PostBatchAsync(Guid sessionId, IngestBatchDto batch, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync($"api/sessions/{sessionId}/batches", batch, Json.IngestBatchDto, cancellationToken);
        if ((int)response.StatusCode is >= 400 and < 500) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(Json.IngestBatchResultDto, cancellationToken);
    }

    /// <summary>A write link for one highlight snapshot; null when there is no image storage or today's cap is reached.</summary>
    public async Task<SnapshotUploadDto?> CreateSnapshotUploadAsync(DateOnly localDay, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsync($"api/snapshots?day={localDay:yyyy-MM-dd}", content: null, cancellationToken);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync(Json.SnapshotUploadDto, cancellationToken) : null;
    }

    public async Task<IReadOnlyList<MomentDto>> GetMomentsAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"api/sessions/{sessionId}/moments", cancellationToken);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync(Json.ListMomentDto, cancellationToken) ?? [] : [];
    }

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
    }

    public async Task<ObserverRuntimeStateDto?> GetObserverStateAsync(CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync("api/observer/state", Json.ObserverRuntimeStateDto, cancellationToken);

    public async Task<IngestObservationResultDto?> IngestObservationAsync(IngestObservationRequestDto request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("api/observer/ingest", request, Json.IngestObservationRequestDto, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(Json.IngestObservationResultDto, cancellationToken);
    }

    public async Task<DailyChapterDto?> GetChapterAsync(DateOnly date, NarrativeMode mode = NarrativeMode.Prose, CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync($"api/archives/{date:yyyy-MM-dd}?mode={mode}", Json.DailyChapterDto, cancellationToken);

    public async Task<BlobAccessDescriptorDto?> GetBlobUploadAccessForPathAsync(string blobPath, CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync($"api/blobs/sas?blobPath={Uri.EscapeDataString(blobPath)}&upload=true", Json.BlobAccessDescriptorDto, cancellationToken);

    /// <summary>
    /// Get a signed read URL for a blob directly from the /read endpoint (preferred method).
    /// </summary>
    public async Task<BlobAccessDescriptorDto?> GetBlobReadAccessAsync(string blobPath, CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync($"api/blobs/read?blobPath={Uri.EscapeDataString(blobPath)}", Json.BlobAccessDescriptorDto, cancellationToken);

    public async Task<IReadOnlyList<SubjectProfileDto>> GetSubjectsAsync(CancellationToken cancellationToken = default)
    {
        var items = await httpClient.GetFromJsonAsync("api/identity/subjects", Json.ListSubjectProfileDto, cancellationToken);
        return items ?? [];
    }

    public async Task<SubjectProfileDto?> RegisterSubjectAsync(RegisterSubjectRequestDto request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("api/identity/subjects", request, Json.RegisterSubjectRequestDto, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(Json.SubjectProfileDto, cancellationToken);
    }

    public async Task<IReadOnlyList<SubjectLiveStatusDto>> GetLiveDashboardStatusAsync(CancellationToken cancellationToken = default)
    {
        var items = await httpClient.GetFromJsonAsync("api/identity/subjects/live-status", Json.ListSubjectLiveStatusDto, cancellationToken);
        return items ?? [];
    }

    public async Task<IdentityRevisionResultDto?> RenameSubjectAsync(string subjectId, RenameSubjectRequestDto request, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Patch, $"api/identity/subjects/{Uri.EscapeDataString(subjectId)}")
        {
            Content = JsonContent.Create(request, Json.RenameSubjectRequestDto)
        };

        using var response = await httpClient.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(Json.IdentityRevisionResultDto, cancellationToken);
    }

    public async Task<IdentityRevisionResultDto?> MergeIdentityAsync(MergeIdentityRequestDto request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("api/identity/merge", request, Json.MergeIdentityRequestDto, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(Json.IdentityRevisionResultDto, cancellationToken);
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

    public async Task<IReadOnlyList<SubjectDriftStatusDto>> GetDriftStatusAsync(CancellationToken cancellationToken = default)
    {
        var items = await httpClient.GetFromJsonAsync("api/identity/subjects/live-risk", Json.ListSubjectDriftStatusDto, cancellationToken);
        return items ?? [];
    }

    public async Task<StorageResetResultDto?> ClearAllDataAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsync("api/diagnostics/reset", null, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(Json.StorageResetResultDto, cancellationToken);
    }
}
