namespace PoWatch.Domain.Models;

public sealed class ObservationEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public DateTimeOffset ObservedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public SubjectId SubjectId { get; init; } = SubjectId.None;

    public string SubjectDisplayName { get; init; } = string.Empty;

    public string Activity { get; init; } = string.Empty;

    public string ClinicalDescription { get; init; } = string.Empty;

    public bool IsSignificant { get; init; }

    public string? SignificantReason { get; init; }

    public bool IsClinicalOutlier { get; init; }

    public string? ImageReference { get; init; }
}
