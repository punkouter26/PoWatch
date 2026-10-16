namespace PoWatch.Domain.Models;

/// <summary>
/// A recap someone chose to share: a frozen copy of the day's recap behind an unguessable token,
/// readable without signing in until it expires or is revoked.
/// </summary>
public sealed record RecapShare
{
    public required string Token { get; init; }
    public required string UserId { get; init; }
    public required DateOnly Day { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset ExpiresUtc { get; init; }

    /// <summary>The recap as it was when shared, serialised; later edits or deletions do not change it.</summary>
    public required string Content { get; init; }

    public bool IsLive(DateTimeOffset nowUtc) => nowUtc < ExpiresUtc;
}
