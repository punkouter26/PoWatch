using PoWatch.Domain.Models;
using PoWatch.Shared.Models;

namespace PoWatch.Application.Mappers;

/// <summary>
/// The single Domain → DTO mapping for observation events. This mapping was previously
/// hand-copied in IdentityService (twice), ReportService, and the (since removed) SSE endpoint —
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
        ImageReference = e.ImageReference
    };

    public static List<ObservationEventDto> ToDtos(this IEnumerable<ObservationEvent> events) =>
        events.Select(ToDto).ToList();
}
