namespace PoWatch.Domain.Models;

public sealed class SubjectProfile
{
    public SubjectId SubjectId { get; init; } = SubjectId.None;

    public string DisplayName { get; set; } = string.Empty;

    public IdentityStatus IdentityStatus { get; set; }

    public DateTimeOffset FirstSeenUtc { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastSeenUtc { get; set; } = DateTimeOffset.UtcNow;

    // Cached last-observed activity — updated after every persisted observation to avoid
    // a cross-partition table scan in the ingest redundancy check.
    public string? LastActivity { get; set; }

    public bool LastActivityIsOutlier { get; set; }
}
