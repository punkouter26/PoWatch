namespace PoWatch.Application.Models;

public sealed class IngestObservationResult
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
