namespace PoWatch.Domain.Models;

public sealed class DailyChapter
{
    public required DateOnly Date { get; init; }

    public required IReadOnlyList<ObservationEvent> Timeline { get; init; }

    public required IReadOnlyList<ObservationEvent> Highlights { get; init; }

    public required string ClinicalNarrative { get; init; }

    // Counts the narrative is built from, exposed so the UI can show them as a stat row instead of
    // making the caregiver parse them back out of a sentence — and so the two can never disagree.
    public int TotalEvents { get; init; }

    public int OutlierCount { get; init; }

    /// <summary>Significant events that are not also clinical outliers, so the two never double-count.</summary>
    public int NotableCount { get; init; }

    public int SubjectCount { get; init; }

    public DateTimeOffset? FirstEventUtc { get; init; }

    public DateTimeOffset? LastEventUtc { get; init; }
}
