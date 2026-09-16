namespace PoWatch.Shared.Models;

/// <summary>Metadata for a voice memo attached to a shift handoff. Audio bytes are streamed
/// separately via <c>GET /api/handoff/memos/{id}/audio</c>; the DTO is intentionally
/// audio-byte-free so list responses stay small.</summary>
public sealed class HandoffMemoDto
{
    public required string Id { get; init; }
    public string? SubjectId { get; init; }
    public required int DurationMs { get; init; }
    public required string ContentType { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public string? AuthorUserId { get; init; }
}
