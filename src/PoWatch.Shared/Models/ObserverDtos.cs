namespace PoWatch.Shared.Models;

public sealed class ObserverRuntimeStateDto
{
    public bool ObservationLoopEnabled { get; init; }
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
    /// <summary>
    /// Optional client-supplied idempotency token. When set it becomes the observation's stable Id, so a
    /// retried/duplicated submission of the same capture collapses to one row instead of creating duplicates.
    /// </summary>
    public Guid? IdempotencyKey { get; init; }
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
    /// <summary>Alert threshold rules that fired during this ingest cycle.</summary>
    public IReadOnlyList<ThresholdAlertDto> TriggeredAlerts { get; init; } = [];
}

public sealed class ThresholdAlertDto
{
    public string RuleName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string SubjectId { get; init; } = string.Empty;
    public DateTimeOffset TriggeredAtUtc { get; init; }
}

/// <summary>
/// Request DTO for acknowledging significant events. Lives in PoWatch.Shared (not the API slice) so it
/// is a first-class cross-boundary contract available to the client and the source-gen JSON context.
/// </summary>
public sealed class AcknowledgeEventsRequestDto
{
    /// <summary>Event IDs to acknowledge.</summary>
    public required IReadOnlyList<string> EventIds { get; init; }

    /// <summary>Identifier of the person acknowledging (e.g., nurse ID, username).</summary>
    public required string AcknowledgedBy { get; init; }

    /// <summary>Optional note explaining the acknowledgment.</summary>
    public string? Note { get; init; }
}
