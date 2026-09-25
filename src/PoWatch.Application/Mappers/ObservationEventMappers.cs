using PoWatch.Domain.Models;
using PoWatch.Domain.Services;
using PoWatch.Shared.Models;

namespace PoWatch.Application.Mappers;

/// <summary>
/// The single Domain → DTO mapping for observation events. This mapping was previously
/// hand-copied in IdentityService (twice), the (since removed) handoff report, and the (since removed) SSE endpoint —
/// four places to forget a new field. Add a field to <see cref="ObservationEventDto"/> here once.
/// </summary>
public static class ObservationEventMappers
{
    public static ObservationEventDto ToDto(this ObservationEvent e) => new()
    {
        Id = (Guid)e.Id,
        ObservedAtUtc = e.ObservedAtUtc,
        SubjectId = e.SubjectId,
        SubjectDisplayName = e.SubjectDisplayName,
        Activity = e.Activity,
        ClinicalDescription = e.ClinicalDescription,
        IsSignificant = e.IsSignificant,
        SignificantReason = e.SignificantReason,
        IsClinicalOutlier = e.IsClinicalOutlier,
        ImageReference = e.ImageReference,
        SignificanceScore = e.SignificanceScore,
        SignificanceConfidence = e.SignificanceConfidence
    };

    public static List<ObservationEventDto> ToDtos(this IEnumerable<ObservationEvent> events) =>
        events.Select(ToDto).ToList();

    /// <summary>
    /// Map the Domain band enum to the Shared cross-boundary enum. The two stay in step by name
    /// and ordinal value; this method exists so a future reorder of one does not silently invert
    /// the other at the boundary.
    /// </summary>
    public static Shared.Models.ActivitySignificance ToShared(this Domain.Services.ActivitySignificance band) => band switch
    {
        Domain.Services.ActivitySignificance.Urgent => Shared.Models.ActivitySignificance.Urgent,
        Domain.Services.ActivitySignificance.Notable => Shared.Models.ActivitySignificance.Notable,
        _ => Shared.Models.ActivitySignificance.Routine
    };
}
