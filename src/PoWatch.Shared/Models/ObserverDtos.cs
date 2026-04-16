namespace PoWatch.Shared.Models;

public sealed class ObserverRuntimeStateDto
{
    public bool ObservationLoopEnabled { get; init; }
    public bool TtsAnnouncementsEnabled { get; init; }
    public bool SaveSignificantImages { get; init; }
    public bool DeveloperModeEnabled { get; init; }
    public int PollIntervalSeconds { get; init; }
    public DateTimeOffset CapturedAtUtc { get; init; }
    public string Status { get; init; } = string.Empty;
    public string StatusDetail { get; init; } = string.Empty;
}

public sealed class IngestObservationRequestDto
{
    // ObservedAtUtc is accepted from the client but overridden server-side in ObservationService for integrity.
    public DateTimeOffset ObservedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string? SubjectHint { get; init; }
    // No defaults — partial/empty bodies must be explicit to prevent silent phantom observations.
    public string Activity { get; init; } = string.Empty;
    public string ClinicalPayload { get; init; } = string.Empty;
    public bool IsSignificant { get; init; }
    public string? SignificantReason { get; init; }
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
    public string? ImageReference { get; init; }
    public string Detail { get; init; } = string.Empty;
}
