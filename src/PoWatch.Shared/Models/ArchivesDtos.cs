namespace PoWatch.Shared.Models;

/// <summary>Discrete band of attention an observed activity deserves. Mirrors
/// <c>PoWatch.Domain.Services.ActivitySignificance</c> so the cross-boundary DTO can carry the
/// classifier's verdict without taking a dependency on Domain. Mapped at the boundary.</summary>
public enum ActivitySignificance
{
    Routine = 0,
    Notable = 1,
    Urgent = 2,
}

/// <summary>How <see cref="DailyChapterDto.ClinicalNarrative"/> should be rendered alongside the
/// timeline. The two modes carry the same data; one speaks it, the other tabulates it.</summary>
public enum NarrativeMode
{
    /// <summary>One paragraph that reads end-to-end. Existing default — never breaks older clients.</summary>
    Prose = 0,

    /// <summary>Per-event rows with a time, a subject, an activity and a band tag. Always present
    /// in the response (so a toggle on the client doesn't refetch); the server still builds it
    /// when mode=Structured so client-side toggling is just a render switch.</summary>
    Structured = 1,
}

/// <summary>One row in the structured-prose narrative. Time is local to the caregiver's clock
/// because every other figure on the chapter uses local time (AGENT.md: culture-sensitive
/// formatting is a correctness issue here).</summary>
public sealed class StructuredNarrativeRowDto
{
    public required DateTimeOffset ObservedAtUtcLocal { get; init; }
    public required string SubjectDisplayName { get; init; }
    public required string Activity { get; init; }
    public required ActivitySignificance Level { get; init; }
    public string? SignificantReason { get; init; }
}

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
    /// <summary>Strength of the underlying signal in [0.0, 1.0]. Default 0.0 mirrors the legacy
    /// reading for pre-scoring rows. Independent of <see cref="IsSignificant"/>.</summary>
    public double SignificanceScore { get; init; }
    /// <summary>Classifier's confidence in the chosen band, in [0.0, 1.0]. Default 1.0 keeps the
    /// legacy reading where the model was assumed fully confident.</summary>
    public double SignificanceConfidence { get; init; }
}

public sealed class DailyChapterDto
{
    public DateOnly Date { get; init; }
    public IReadOnlyList<ObservationEventDto> Timeline { get; init; } = [];
    public IReadOnlyList<ObservationEventDto> Highlights { get; init; } = [];
    public string ClinicalNarrative { get; init; } = string.Empty;

    /// <summary>Tabulated version of the same data the prose covers. Populated for every request
    /// (regardless of <see cref="Mode"/>) so toggling between Prose and Structured on the client
    /// never triggers a refetch — the rendering is the only thing that changes. Empty when the
    /// day had no observations.</summary>
    public IReadOnlyList<StructuredNarrativeRowDto> StructuredRows { get; init; } = [];

    /// <summary>Which narrative form <see cref="ClinicalNarrative"/> was rendered as. Echoes the
    /// request so the client can stay in sync if a future feature filters by mode server-side.</summary>
    public NarrativeMode Mode { get; init; }

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
