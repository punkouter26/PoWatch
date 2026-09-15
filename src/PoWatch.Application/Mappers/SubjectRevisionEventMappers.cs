using PoWatch.Domain.Models;
using PoWatch.Shared.Models;

namespace PoWatch.Application.Mappers;

/// <summary>Domain → DTO mapping for subject revision events. Kept distinct from
/// <see cref="ObservationEventMappers"/> so the two audit surfaces (observations vs identity
/// revisions) do not accidentally share a conversion that one of them silently changes.</summary>
public static class SubjectRevisionEventMappers
{
    public static SubjectRevisionEventDto ToDto(this SubjectRevisionEvent e) => new()
    {
        OccurredAtUtc = e.OccurredAtUtc,
        Kind = e.Kind switch
        {
            Domain.Models.SubjectRevisionKind.Created => Shared.Models.SubjectRevisionKind.Created,
            Domain.Models.SubjectRevisionKind.Renamed => Shared.Models.SubjectRevisionKind.Renamed,
            Domain.Models.SubjectRevisionKind.MergedInto => Shared.Models.SubjectRevisionKind.MergedInto,
            Domain.Models.SubjectRevisionKind.MergedFrom => Shared.Models.SubjectRevisionKind.MergedFrom,
            Domain.Models.SubjectRevisionKind.Deleted => Shared.Models.SubjectRevisionKind.Deleted,
            _ => Shared.Models.SubjectRevisionKind.Created
        },
        Detail = e.Detail,
        ActorUserId = e.ActorUserId
    };

    public static List<SubjectRevisionEventDto> ToDtos(this IEnumerable<SubjectRevisionEvent> events) =>
        events.Select(ToDto).ToList();
}
