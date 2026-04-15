namespace PoWatch.Application.Models;

public sealed class IngestObservationRequest
{
    public DateTimeOffset ObservedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public string? SubjectHint { get; init; }

    public string Activity { get; init; } = "Unknown";

    public string ClinicalPayload { get; init; } = string.Empty;

    public bool IsSignificant { get; init; }

    public string? SignificantReason { get; init; }
}
