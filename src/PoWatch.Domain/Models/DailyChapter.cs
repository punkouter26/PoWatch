namespace PoWatch.Domain.Models;

public sealed class DailyChapter
{
    public required DateOnly Date { get; init; }

    public required IReadOnlyList<ObservationEvent> Timeline { get; init; }

    public required IReadOnlyList<ObservationEvent> Highlights { get; init; }

    public required string ClinicalNarrative { get; init; }
}
