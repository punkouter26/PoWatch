namespace PoWatch.Domain.Models;

/// <summary>
/// A single-purpose signed URL that lets a family member view an anonymised snapshot of a
/// caregiver-recorded day. The link is the entire security model: it is anonymous, expires,
/// can be revoked, and the data it exposes has already been anonymised through
/// <c>SubjectDisplayNames.Humanize</c>. Snapshot is taken at creation time so revocation
/// does not invalidate historical views — the snapshot is what the viewer sees.
/// </summary>
public sealed class ShareLink
{
    /// <summary>The opaque id used in the URL. 32 hex characters (Guid.NewGuid().ToString("N"))
    /// — long enough to resist enumeration, short enough to type from a phone.</summary>
    public required string Id { get; init; }

    /// <summary>The local calendar day the snapshot covers.</summary>
    public required DateOnly Date { get; init; }

    /// <summary>Server timestamp at creation.</summary>
    public required DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>Hard expiry. 24 hours from creation per product decision.</summary>
    public required DateTimeOffset ExpiresAtUtc { get; init; }

    /// <summary>The user who created the link. Null when the system generated it (no such path
    /// today, but the column is here so a future scheduled-share feature can leave an audit row).</summary>
    public string? CreatedByUserId { get; init; }

    /// <summary>When the link was explicitly revoked. The link remains in storage until the
    /// retention sweep deletes it; <see cref="IsUsable"/> is the gate the read path uses.</summary>
    public DateTimeOffset? RevokedAtUtc { get; set; }

    /// <summary>The anonymised snapshot rendered for the viewer. Captured at creation time so
    /// revoking the link or changes to the underlying chapter do not retroactively edit what
    /// the viewer has already seen.</summary>
    public required string AnonymisedNarrative { get; set; }

    /// <summary>True when the link is past expiry OR explicitly revoked. The read path returns
    /// 410 Gone for either state.</summary>
    public bool IsUsable => RevokedAtUtc is null && DateTimeOffset.UtcNow < ExpiresAtUtc;

    public const int DefaultTtlHours = 24;
}
