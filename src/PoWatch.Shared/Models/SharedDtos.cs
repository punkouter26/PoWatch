namespace PoWatch.Shared.Models;

// Observer

public sealed class ObserverRuntimeStateDto
{
    public bool ObservationLoopEnabled { get; init; }
    public bool TtsAnnouncementsEnabled { get; init; }
    public bool SaveSignificantImages { get; init; }
    public int PollIntervalSeconds { get; init; }
    public DateTimeOffset CapturedAtUtc { get; init; }
    public string Status { get; init; } = string.Empty;
    public string StatusDetail { get; init; } = string.Empty;
}

public sealed class IngestObservationRequestDto
{
    public DateTimeOffset ObservedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string? SubjectHint { get; init; }
    public string Activity { get; init; } = "Desk Work";
    public string ClinicalPayload { get; init; } = "<S>Subject working at desk<E>";
    public bool IsSignificant { get; init; } = true;
    public string? SignificantReason { get; init; } = "State change";
}

public sealed class IngestObservationResultDto
{
    public bool Accepted { get; init; }
    public bool Dropped { get; init; }
    public bool IsOutlier { get; init; }
    public bool SkippedAsRedundant { get; init; }
    public string? EventId { get; init; }
    public string SubjectId { get; init; } = string.Empty;
    public string SubjectDisplayName { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}

// Archives

public sealed class ObservationEventDto
{
    public Guid Id { get; init; }
    public DateTimeOffset ObservedAtUtc { get; init; }
    public string SubjectId { get; init; } = string.Empty;
    public string SubjectDisplayName { get; init; } = string.Empty;
    public string Activity { get; init; } = string.Empty;
    public string ClinicalDescription { get; init; } = string.Empty;
    public bool IsSignificant { get; init; }
    public string? SignificantReason { get; init; }
    public bool IsClinicalOutlier { get; init; }
    public string? ImageReference { get; init; }
}

public sealed class DailyChapterDto
{
    public DateOnly Date { get; init; }
    public IReadOnlyList<ObservationEventDto> Timeline { get; init; } = [];
    public IReadOnlyList<ObservationEventDto> Highlights { get; init; } = [];
    public string ClinicalNarrative { get; init; } = string.Empty;
}

// Identity

public sealed class SubjectProfileDto
{
    public string SubjectId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool IsKnownIdentity { get; init; }
    public DateTimeOffset FirstSeenUtc { get; init; }
    public DateTimeOffset LastSeenUtc { get; init; }
}

public sealed class MergeIdentityRequestDto
{
    public string PrimarySubjectId { get; init; } = string.Empty;
    public string SecondarySubjectId { get; init; } = string.Empty;
    public string? NewDisplayName { get; init; }
}

public sealed class RenameSubjectRequestDto
{
    public string NewName { get; init; } = string.Empty;
}

public sealed class IdentityRevisionResultDto
{
    public string CanonicalSubjectId { get; init; } = string.Empty;
    public string CanonicalName { get; init; } = string.Empty;
    public int EventsRewritten { get; init; }
    public int SubjectsRemoved { get; init; }
}

// Diagnostics

public sealed class DiagnosticsSnapshotDto
{
    public DateTimeOffset CapturedAtUtc { get; init; }
    public double CpuLoadPercent { get; init; }
    public double MemoryMb { get; init; }
    public string StorageConnectionStatus { get; init; } = string.Empty;
    public string MaskedEndpoint { get; init; } = string.Empty;
    public string MaskedApiKey { get; init; } = string.Empty;
}

// Blobs

public sealed class BlobAccessDescriptorDto
{
    public string SasUrl { get; init; } = string.Empty;
    public string BlobPath { get; init; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; init; }
}
