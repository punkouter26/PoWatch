namespace PoWatch.Shared.Models;

/// <summary>Metadata for a family share link. Never carries the anonymised snapshot — that is
/// rendered only on the read path through a separate, anonymous endpoint.</summary>
public sealed class ShareLinkSummaryDto
{
    public required string Id { get; init; }
    public required DateOnly Date { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required DateTimeOffset ExpiresAtUtc { get; init; }
    public DateTimeOffset? RevokedAtUtc { get; init; }
}

/// <summary>The view a family member sees when they open the link. The narrative has already been
/// humanised; subject names appear as "Person N", not as raw ids. No raw event data is exposed.</summary>
public sealed class ShareLinkViewDto
{
    public required DateOnly Date { get; init; }
    public required DateTimeOffset ExpiresAtUtc { get; init; }
    public required string AnonymisedNarrative { get; init; }
}

/// <summary>Request body for creating a share link. Both fields are optional so the API can pick
/// defaults: today's date, 24h TTL.</summary>
public sealed class CreateShareLinkRequestDto
{
    public DateOnly? Date { get; init; }
    public int? TtlHours { get; init; }
}
