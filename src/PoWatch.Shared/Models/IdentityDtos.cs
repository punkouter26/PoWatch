namespace PoWatch.Shared.Models;

public sealed class SubjectProfileDto
{
    public string SubjectId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool IsKnownIdentity { get; init; }
    public DateTimeOffset FirstSeenUtc { get; init; }
    public DateTimeOffset LastSeenUtc { get; init; }
}

public sealed class MergeIdentityRequestDto
{
    public string PrimarySubjectId { get; init; } = string.Empty;
    public string SecondarySubjectId { get; init; } = string.Empty;
    public string? NewDisplayName { get; init; }
}

public sealed class RenameSubjectRequestDto
{
    public string NewName { get; init; } = string.Empty;
}

public sealed class IdentityRevisionResultDto
{
    public string CanonicalSubjectId { get; init; } = string.Empty;
    public string CanonicalName { get; init; } = string.Empty;
    public int EventsRewritten { get; init; }
    public int SubjectsRemoved { get; init; }
}

public sealed class RegisterSubjectRequestDto
{
    public string DisplayName { get; init; } = string.Empty;
}

/// <summary>The kind of revision a subject underwent. Mirrors
/// <c>PoWatch.Domain.Models.SubjectRevisionKind</c>; ordinal values must stay in step.</summary>
public enum SubjectRevisionKind
{
    Created = 0,
    Renamed = 1,
    MergedInto = 2,
    MergedFrom = 3,
    Deleted = 4,
}

/// <summary>One row in a subject's audit history. Chronologically ordered server-side.</summary>
public sealed class SubjectRevisionEventDto
{
    public required DateTimeOffset OccurredAtUtc { get; init; }
    public required SubjectRevisionKind Kind { get; init; }
    public string? Detail { get; init; }
    public string? ActorUserId { get; init; }
}
