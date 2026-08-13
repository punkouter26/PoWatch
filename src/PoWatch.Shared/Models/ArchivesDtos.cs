namespace PoWatch.Shared.Models;

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

    // Mirrors PoWatch.Domain.Models.DailyChapter — the endpoint serializes the domain type and the
    // client deserializes it as this DTO, so the two shapes have to stay in step.
    public int TotalEvents { get; init; }
    public int OutlierCount { get; init; }

    /// <summary>Significant events that are not also clinical outliers, so the two never double-count.</summary>
    public int NotableCount { get; init; }
    public int SubjectCount { get; init; }
    public DateTimeOffset? FirstEventUtc { get; init; }
    public DateTimeOffset? LastEventUtc { get; init; }
}
