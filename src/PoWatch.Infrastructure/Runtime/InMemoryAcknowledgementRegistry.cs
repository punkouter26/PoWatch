using System.Collections.Concurrent;
using PoWatch.Application.Contracts;

namespace PoWatch.Infrastructure.Runtime;

/// <summary>
/// In-process acknowledgement registry backed by a concurrent dictionary.
/// Provides O(1) read performance for the live dashboard unacknowledged count.
/// Acknowledgements survive the hosting process lifetime but are not persisted across restarts.
/// </summary>
public sealed class InMemoryAcknowledgementRegistry : IAcknowledgementRegistry
{
    // Key: event ID — Value: acknowledgedBy identifier
    private readonly ConcurrentDictionary<Guid, string> _acknowledged = new();

    /// <inheritdoc/>
    public void Acknowledge(IReadOnlyList<Guid> eventIds, string acknowledgedBy)
    {
        foreach (var id in eventIds)
            _acknowledged.TryAdd(id, acknowledgedBy);
    }

    /// <inheritdoc/>
    public bool IsAcknowledged(Guid eventId) => _acknowledged.ContainsKey(eventId);
}
