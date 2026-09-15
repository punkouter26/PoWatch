namespace PoWatch.Domain.Models;

public sealed class ObservationEvent
{
    public ObservationEventId Id { get; init; } = ObservationEventId.New();

    public DateTimeOffset ObservedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public SubjectId SubjectId { get; init; } = SubjectId.None;

    public string SubjectDisplayName { get; init; } = string.Empty;

    public string Activity { get; init; } = string.Empty;

    public string ClinicalDescription { get; init; } = string.Empty;

    public bool IsSignificant { get; init; }

    public string? SignificantReason { get; init; }

    public bool IsClinicalOutlier { get; init; }

    public string? ImageReference { get; init; }

    /// <summary>Strength of the underlying signal in [0.0, 1.0]. Always set by the classifier — even when
    /// the band itself is asserted by the caller (dev-tool injectors, contract tests). Default 0.0
    /// preserves the historical reading of pre-scoring observations on legacy rows.</summary>
    public double SignificanceScore { get; init; }

    /// <summary>Classifier's confidence in the chosen band, in [0.0, 1.0]. Discounted by short input
    /// and by low letter density. Independent of <see cref="SignificanceScore"/>. Default 1.0 keeps
    /// the legacy reading where the model was assumed fully confident.</summary>
    public double SignificanceConfidence { get; init; }
}
