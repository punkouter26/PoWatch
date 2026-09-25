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
[JsonSerializable(typeof(DailyChapterDto))]
[JsonSerializable(typeof(StructuredNarrativeRowDto))]
[JsonSerializable(typeof(NarrativeMode))]
[JsonSerializable(typeof(BlobAccessDescriptorDto))]
[JsonSerializable(typeof(SubjectProfileDto))]
[JsonSerializable(typeof(List<SubjectProfileDto>))]
[JsonSerializable(typeof(SubjectRevisionEventDto))]
[JsonSerializable(typeof(List<SubjectRevisionEventDto>))]
[JsonSerializable(typeof(SubjectRevisionKind))]
[JsonSerializable(typeof(RegisterSubjectRequestDto))]
[JsonSerializable(typeof(SubjectLiveStatusDto))]
[JsonSerializable(typeof(List<SubjectLiveStatusDto>))]
[JsonSerializable(typeof(IdentityRevisionResultDto))]
[JsonSerializable(typeof(RenameSubjectRequestDto))]
[JsonSerializable(typeof(MergeIdentityRequestDto))]
[JsonSerializable(typeof(DiagnosticsSnapshotDto))]
[JsonSerializable(typeof(SubjectDriftStatusDto))]
[JsonSerializable(typeof(List<SubjectDriftStatusDto>))]
[JsonSerializable(typeof(StorageResetResultDto))]
[JsonSerializable(typeof(AuthStateDto))]
[JsonSerializable(typeof(AuthConfigDto))]
[JsonSerializable(typeof(HealthReportDto))]
[JsonSerializable(typeof(StartSessionRequestDto))]
[JsonSerializable(typeof(SessionDto))]
[JsonSerializable(typeof(List<SessionDto>))]
[JsonSerializable(typeof(IngestBatchDto))]
[JsonSerializable(typeof(IngestBatchResultDto))]
[JsonSerializable(typeof(PixelSamplePayload))]
[JsonSerializable(typeof(PresenceStatsDto))]
[JsonSerializable(typeof(SpaceStatsDto))]
[JsonSerializable(typeof(ObjectStatsDto))]
[JsonSerializable(typeof(PatternStatsDto))]
[JsonSerializable(typeof(EnvironmentStatsDto))]
[JsonSerializable(typeof(PipelineStatsDto))]
[JsonSerializable(typeof(StatsChangedDto))]
[JsonSerializable(typeof(SnapshotUploadDto))]
[JsonSerializable(typeof(TrophyCabinetDto))]
[JsonSerializable(typeof(AchievementsUnlockedDto))]
[JsonSerializable(typeof(RegularDto))]
[JsonSerializable(typeof(List<RegularDto>))]
[JsonSerializable(typeof(ObserveRegularRequestDto))]
[JsonSerializable(typeof(ObserveRegularResultDto))]
[JsonSerializable(typeof(RenameRegularRequestDto))]
[JsonSerializable(typeof(MergeRegularsRequestDto))]
[JsonSerializable(typeof(List<MomentDto>))]
[JsonSerializable(typeof(DetectionsPayload))]
internal sealed partial class PoWatchJsonContext : JsonSerializerContext;
