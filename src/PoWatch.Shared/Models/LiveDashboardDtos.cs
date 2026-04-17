namespace PoWatch.Shared.Models;

/// <summary>Per-subject live status snapshot for the multi-subject dashboard.</summary>
public sealed class SubjectLiveStatusDto
{
    public string SubjectId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool IsKnownIdentity { get; init; }
    public DateTimeOffset LastSeenUtc { get; init; }
    public string LastActivity { get; init; } = string.Empty;
    public bool LastActivityIsOutlier { get; init; }
    public int UnacknowledgedSignificantCount { get; init; }
    public IReadOnlyList<ObservationEventDto> RecentEvents { get; init; } = [];
}
