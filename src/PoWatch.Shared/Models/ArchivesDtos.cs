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
}
