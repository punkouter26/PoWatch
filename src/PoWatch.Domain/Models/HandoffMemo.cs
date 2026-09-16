namespace PoWatch.Domain.Models;

/// <summary>
/// One caregiver voice memo attached to a shift handoff. The audio bytes live in Blob Storage
/// (path on <see cref="BlobPath"/>); the row carries only the metadata needed to locate and
/// render the memo in the handoff dialog. Lives in Domain because it is a domain concept —
/// caregivers leave memos at handoff — and the storage shape is the responsibility of
/// Infrastructure.
/// </summary>
public sealed class HandoffMemo
{
    public required string Id { get; init; }

    /// <summary>Optional subject this memo is about. Null when the memo is about the shift in
    /// general (the common case).</summary>
    public SubjectId? SubjectId { get; init; }

    /// <summary>Path inside the memos blob container where the audio bytes live.</summary>
    public required string BlobPath { get; init; }

    /// <summary>Audio duration in milliseconds. Self-reported by the recorder; the server does
    /// not transcode or measure audio (privacy + simplicity).</summary>
    public int DurationMs { get; init; }

    /// <summary>Audio MIME type as captured by the browser (audio/webm;codecs=opus on Chrome/Edge,
    /// audio/ogg on Firefox, audio/mp4 on Safari). Recorded so the playback element picks the
    /// right codec and the format is preserved across browser upgrades.</summary>
    public string ContentType { get; init; } = "audio/webm";

    /// <summary>When the caregiver pressed stop. Server-authoritative.</summary>
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>The user who recorded it, when known. Mirrors the audit-trail pattern used by
    /// SubjectRevisionEvent.ActorUserId so the same actor resolution logic can apply.</summary>
    public string? AuthorUserId { get; init; }

    /// <summary>Hard-coded 30-day retention per product decision. Exposed as a public constant so
    /// tests and the prune job reference the same number — drift here would silently delete memos
    /// earlier or later than the product promise.</summary>
    public const int RetentionDays = 30;
}
