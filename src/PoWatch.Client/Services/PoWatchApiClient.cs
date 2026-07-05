using System.Net.Http.Json;
using PoWatch.Shared.Models;

namespace PoWatch.Client.Services;

public sealed class PoWatchApiClient(HttpClient httpClient)
{
    private static readonly PoWatchJsonContext Json = PoWatchJsonContext.Default;

    public async Task<ObserverRuntimeStateDto?> GetObserverStateAsync(CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync("api/observer/state", Json.ObserverRuntimeStateDto, cancellationToken);

    public async Task<IngestObservationResultDto?> IngestObservationAsync(IngestObservationRequestDto request, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("api/observer/ingest", request, Json.IngestObservationRequestDto, cancellationToken);
        return await response.Content.ReadFromJsonAsync(Json.IngestObservationResultDto, cancellationToken);
    }

    public async Task<DailyChapterDto?> GetChapterAsync(DateOnly date, CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync($"api/archives/{date:yyyy-MM-dd}", Json.DailyChapterDto, cancellationToken);

    public async Task<BlobAccessDescriptorDto?> GetBlobUploadAccessAsync(string subjectId, DateOnly date, CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync($"api/blobs/sas?subjectId={Uri.EscapeDataString(subjectId)}&date={date:yyyyMMdd}", Json.BlobAccessDescriptorDto, cancellationToken);

    public async Task<BlobAccessDescriptorDto?> GetBlobUploadAccessForPathAsync(string blobPath, CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync($"api/blobs/sas?blobPath={Uri.EscapeDataString(blobPath)}&upload=true", Json.BlobAccessDescriptorDto, cancellationToken);

    public async Task<string?> GetBlobReadUrlAsync(string blobPath, CancellationToken cancellationToken = default)
    {
        var access = await httpClient.GetFromJsonAsync($"api/blobs/sas?blobPath={Uri.EscapeDataString(blobPath)}", Json.BlobAccessDescriptorDto, cancellationToken);
        return access?.SasUrl;
    }

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
        var response = await httpClient.PostAsJsonAsync("api/identity/subjects", request, Json.RegisterSubjectRequestDto, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
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
        return await response.Content.ReadFromJsonAsync(Json.IdentityRevisionResultDto, cancellationToken);
    }

    public async Task<IdentityRevisionResultDto?> MergeIdentityAsync(MergeIdentityRequestDto request, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("api/identity/merge", request, Json.MergeIdentityRequestDto, cancellationToken);
        return await response.Content.ReadFromJsonAsync(Json.IdentityRevisionResultDto, cancellationToken);
    }

    public async Task<DiagnosticsSnapshotDto?> GetDiagnosticsAsync(CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync("api/diagnostics/status", Json.DiagnosticsSnapshotDto, cancellationToken);

    public string GetHandoffReportUrl(DateOnly date, string shiftWindow) =>
        $"{httpClient.BaseAddress}api/archives/{date:yyyy-MM-dd}/handoff-report?shiftWindow={Uri.EscapeDataString(shiftWindow)}";

    public async Task<IReadOnlyList<SubjectDriftStatusDto>> GetDriftStatusAsync(CancellationToken cancellationToken = default)
    {
        var items = await httpClient.GetFromJsonAsync("api/identity/subjects/live-risk", Json.ListSubjectDriftStatusDto, cancellationToken);
        return items ?? [];
    }

    public async Task<HandoffBriefDto?> GenerateHandoffBriefAsync(DateOnly date, GenerateHandoffBriefRequestDto request, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync($"api/archives/{date:yyyy-MM-dd}/handoff-brief", request, Json.GenerateHandoffBriefRequestDto, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync(Json.HandoffBriefDto, cancellationToken);
    }

    public async Task<StorageResetResultDto?> ClearAllDataAsync(CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsync("api/diagnostics/reset", null, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync(Json.StorageResetResultDto, cancellationToken);
    }
}
