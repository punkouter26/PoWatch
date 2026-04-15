namespace PoWatch.Application.Models;

public sealed class MergeIdentityRequest
{
    public string PrimarySubjectId { get; init; } = string.Empty;

    public string SecondarySubjectId { get; init; } = string.Empty;

    public string? NewDisplayName { get; init; }
}

public sealed class IdentityRevisionResult
{
    public string CanonicalSubjectId { get; init; } = string.Empty;

    public string CanonicalName { get; init; } = string.Empty;

    public int EventsRewritten { get; init; }

    public int SubjectsRemoved { get; init; }
}
