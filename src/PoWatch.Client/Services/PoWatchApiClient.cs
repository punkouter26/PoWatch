using System.Net.Http.Json;
using PoWatch.Shared.Models;

namespace PoWatch.Client.Services;

public sealed class PoWatchApiClient(HttpClient httpClient)
{
    public async Task<ObserverRuntimeStateDto?> GetObserverStateAsync(CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<ObserverRuntimeStateDto>("api/observer/state", cancellationToken);

    public async Task<IngestObservationResultDto?> IngestObservationAsync(IngestObservationRequestDto request, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("api/observer/ingest", request, cancellationToken);
        return await response.Content.ReadFromJsonAsync<IngestObservationResultDto>(cancellationToken: cancellationToken);
    }

    public async Task<DailyChapterDto?> GetChapterAsync(DateOnly date, CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<DailyChapterDto>($"api/archives/{date:yyyy-MM-dd}", cancellationToken);

    public async Task<BlobAccessDescriptorDto?> GetBlobUploadAccessAsync(string subjectId, DateOnly date, CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<BlobAccessDescriptorDto>($"api/blobs/sas?subjectId={Uri.EscapeDataString(subjectId)}&date={date:yyyyMMdd}", cancellationToken);

    public async Task<BlobAccessDescriptorDto?> GetBlobUploadAccessForPathAsync(string blobPath, CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<BlobAccessDescriptorDto>($"api/blobs/sas?blobPath={Uri.EscapeDataString(blobPath)}&upload=true", cancellationToken);

    public async Task<string?> GetBlobReadUrlAsync(string blobPath, CancellationToken cancellationToken = default)
    {
        var access = await httpClient.GetFromJsonAsync<BlobAccessDescriptorDto>($"api/blobs/sas?blobPath={Uri.EscapeDataString(blobPath)}", cancellationToken);
        return access?.SasUrl;
    }

    /// <summary>
    /// Get a signed read URL for a blob directly from the /read endpoint (preferred method).
    /// </summary>
    public async Task<BlobAccessDescriptorDto?> GetBlobReadAccessAsync(string blobPath, CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<BlobAccessDescriptorDto>($"api/blobs/read?blobPath={Uri.EscapeDataString(blobPath)}", cancellationToken);

    /// <summary>
    /// Check integrity and viewability of multiple evidence blobs (for diagnostics/QA).
    /// </summary>
    public async Task<dynamic?> CheckBlobIntegrityAsync(IEnumerable<string> blobPaths, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("api/blobs/integrity", blobPaths, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<dynamic>(cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<SubjectProfileDto>> GetSubjectsAsync(CancellationToken cancellationToken = default)
    {
        var items = await httpClient.GetFromJsonAsync<List<SubjectProfileDto>>("api/identity/subjects", cancellationToken);
        return items ?? [];
    }

    public async Task<SubjectProfileDto?> RegisterSubjectAsync(RegisterSubjectRequestDto request, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("api/identity/subjects", request, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<SubjectProfileDto>(cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<SubjectLiveStatusDto>> GetLiveDashboardStatusAsync(CancellationToken cancellationToken = default)
    {
        var items = await httpClient.GetFromJsonAsync<List<SubjectLiveStatusDto>>("api/identity/subjects/live-status", cancellationToken);
        return items ?? [];
    }

    public async Task<IdentityRevisionResultDto?> RenameSubjectAsync(string subjectId, RenameSubjectRequestDto request, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Patch, $"api/identity/subjects/{Uri.EscapeDataString(subjectId)}")
        {
            Content = JsonContent.Create(request)
        };

        using var response = await httpClient.SendAsync(message, cancellationToken);
        return await response.Content.ReadFromJsonAsync<IdentityRevisionResultDto>(cancellationToken: cancellationToken);
    }

    public async Task<IdentityRevisionResultDto?> MergeIdentityAsync(MergeIdentityRequestDto request, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("api/identity/merge", request, cancellationToken);
        return await response.Content.ReadFromJsonAsync<IdentityRevisionResultDto>(cancellationToken: cancellationToken);
    }

    public async Task<DiagnosticsSnapshotDto?> GetDiagnosticsAsync(CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<DiagnosticsSnapshotDto>("api/diagnostics/status", cancellationToken);

    public string GetHandoffReportUrl(DateOnly date, string shiftWindow) =>
        $"{httpClient.BaseAddress}api/archives/{date:yyyy-MM-dd}/handoff-report?shiftWindow={Uri.EscapeDataString(shiftWindow)}";

    public async Task<IReadOnlyList<SubjectDriftStatusDto>> GetDriftStatusAsync(CancellationToken cancellationToken = default)
    {
        var items = await httpClient.GetFromJsonAsync<List<SubjectDriftStatusDto>>("api/identity/subjects/live-risk", cancellationToken);
        return items ?? [];
    }

    public async Task<HandoffBriefDto?> GenerateHandoffBriefAsync(DateOnly date, GenerateHandoffBriefRequestDto request, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync($"api/archives/{date:yyyy-MM-dd}/handoff-brief", request, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<HandoffBriefDto>(cancellationToken: cancellationToken);
    }

    public async Task<StorageResetResultDto?> ClearAllDataAsync(CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsync("api/diagnostics/reset", null, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<StorageResetResultDto>(cancellationToken: cancellationToken);
    }
}
