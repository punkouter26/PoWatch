namespace PoWatch.Domain.Models;

public sealed class SubjectProfile
{
    public string SubjectId { get; init; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool IsKnownIdentity { get; set; }

    public DateTimeOffset FirstSeenUtc { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastSeenUtc { get; set; } = DateTimeOffset.UtcNow;
}
