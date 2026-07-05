using System.Text.Json.Serialization;
using PoWatch.Shared.Models;

namespace PoWatch.Client.Services;

/// <summary>
/// Source-generated JSON metadata for every type crossing the BFF boundary, so the WASM
/// client serializes without reflection and passes the trim analyzer (rule 6.6).
/// Options mirror <see cref="System.Text.Json.JsonSerializerDefaults.Web"/> to match the API.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(ObserverRuntimeStateDto))]
[JsonSerializable(typeof(IngestObservationRequestDto))]
[JsonSerializable(typeof(IngestObservationResultDto))]
[JsonSerializable(typeof(DailyChapterDto))]
[JsonSerializable(typeof(BlobAccessDescriptorDto))]
[JsonSerializable(typeof(SubjectProfileDto))]
[JsonSerializable(typeof(List<SubjectProfileDto>))]
[JsonSerializable(typeof(RegisterSubjectRequestDto))]
[JsonSerializable(typeof(SubjectLiveStatusDto))]
[JsonSerializable(typeof(List<SubjectLiveStatusDto>))]
[JsonSerializable(typeof(IdentityRevisionResultDto))]
[JsonSerializable(typeof(RenameSubjectRequestDto))]
[JsonSerializable(typeof(MergeIdentityRequestDto))]
[JsonSerializable(typeof(DiagnosticsSnapshotDto))]
[JsonSerializable(typeof(SubjectDriftStatusDto))]
[JsonSerializable(typeof(List<SubjectDriftStatusDto>))]
[JsonSerializable(typeof(HandoffBriefDto))]
[JsonSerializable(typeof(GenerateHandoffBriefRequestDto))]
[JsonSerializable(typeof(StorageResetResultDto))]
[JsonSerializable(typeof(AuthMeResponse))]
[JsonSerializable(typeof(AuthConfigResponse))]
internal sealed partial class PoWatchJsonContext : JsonSerializerContext;

/// <summary>Server auth state from <c>/auth/me</c>.</summary>
internal sealed record AuthMeResponse(bool IsAuthenticated, string? Name, string[]? Roles);

/// <summary>Available sign-in methods from <c>/auth/config</c>.</summary>
internal sealed record AuthConfigResponse(bool MicrosoftEnabled, bool GuestEnabled, string Environment);
