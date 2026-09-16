using PoWatch.Shared.Models;

namespace PoWatch.Client.Services.Outbox;

/// <summary>One queued observation awaiting an offline-tolerant retry. The persisted shape lives
/// in IndexedDB (see outbox-store.js); this is the managed-side mirror.</summary>
public sealed class OutboxEntry
{
    public required long EnqueuedAtUtc { get; init; }
    public required string IdempotencyKey { get; init; }
    public required IngestObservationRequestDto Payload { get; init; }
    public int AttemptCount { get; init; }
    public string? LastError { get; init; }
}
