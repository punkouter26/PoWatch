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
    /// <summary>
    /// The server's significance verdict for this observation. Authoritative — the client renders and
    /// alerts on this rather than on whatever it guessed locally before posting.
    /// </summary>
    public bool IsSignificant { get; init; }
    /// <summary>Plain-language reason the observation was flagged, or null when it is routine.</summary>
    public string? SignificantReason { get; init; }
    /// <summary>Strength of the underlying signal in [0.0, 1.0]. Independent of the discrete band:
    /// a routine observation can still have a non-zero score, and a notable one can have low score.
    /// Informational — used by the client for soft gradients (heat opacity, pattern-bar height).</summary>
    public double SignificanceScore { get; init; }
    /// <summary>Classifier's confidence in the chosen band, in [0.0, 1.0]. Discounted by short input
    /// and by low letter density. Informational — alert gates filter on <see cref="IsSignificant"/>,
    /// never on this number.</summary>
    public double SignificanceConfidence { get; init; }
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

/// <summary>Result of <c>POST /api/observer/acknowledge</c> (audit #7: typed instead of an anonymous object).</summary>
public sealed record AcknowledgeEventsResultDto(int AcknowledgedCount, DateTimeOffset AcknowledgedAtUtc);
