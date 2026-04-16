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
