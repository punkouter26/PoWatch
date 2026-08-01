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
[JsonSerializable(typeof(ModelRegistryEntry[]))]
[JsonSerializable(typeof(ObserverRuntimeStateDto))]
[JsonSerializable(typeof(IngestObservationRequestDto))]
[JsonSerializable(typeof(IngestObservationResultDto))]
[JsonSerializable(typeof(AcknowledgeEventsRequestDto))]
[JsonSerializable(typeof(SubjectBaselineDto))]
[JsonSerializable(typeof(BlobIntegrityCheckDto))]
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
[JsonSerializable(typeof(AcknowledgeEventsResultDto))]
[JsonSerializable(typeof(AuthStateDto))]
[JsonSerializable(typeof(AuthConfigDto))]
[JsonSerializable(typeof(HealthReportDto))]
internal sealed partial class PoWatchJsonContext : JsonSerializerContext;
