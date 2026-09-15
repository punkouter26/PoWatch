namespace PoWatch.Domain.Models;

public sealed class DailyChapter
{
    public required DateOnly Date { get; init; }

    public required IReadOnlyList<ObservationEvent> Timeline { get; init; }

    public required IReadOnlyList<ObservationEvent> Highlights { get; init; }

    public required string ClinicalNarrative { get; init; }

    /// <summary>Tabulated form of the same data the prose covers. Always populated by the service
    /// so a client-side toggle between Prose and Structured never triggers a refetch. The actual
    /// rows live in <see cref="StructuredRows"/>; this collection is empty when the day had
    /// no observations.</summary>
    public IReadOnlyList<Domain.Models.StructuredNarrativeRow> StructuredRows { get; init; } = [];

    /// <summary>Which narrative form the requester asked for. Echoed so the client can stay in
    /// sync if a future feature filters by mode server-side.</summary>
    public Domain.Services.ActivitySignificanceNarrativeMode Mode { get; init; } = Domain.Services.ActivitySignificanceNarrativeMode.Prose;

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

/// <summary>Domain-side row used to build the structured narrative. Distinct from the Shared
/// DTO so Domain does not depend on Shared.</summary>
public sealed class StructuredNarrativeRow
{
    public required DateTimeOffset ObservedAtUtcLocal { get; init; }
    public required string SubjectDisplayName { get; init; }
    public required string Activity { get; init; }
    public required Domain.Services.ActivitySignificance Level { get; init; }
    public string? SignificantReason { get; init; }
}
