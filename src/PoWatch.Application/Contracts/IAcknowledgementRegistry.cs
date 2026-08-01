using PoWatch.Domain.Models;

namespace PoWatch.Application.Contracts;

/// <summary>
/// Thread-safe registry tracking which significant observation events have been acknowledged
/// by clinical staff. Acknowledgement state is in-process-lifetime; the registry is seeded
/// fresh on each restart (no cross-restart persistence requirement at this tier).
/// </summary>
public interface IAcknowledgementRegistry
{
    /// <summary>
    /// Records a batch of event IDs as acknowledged by the specified clinician.
    /// Idempotent: re-acknowledging an event is a no-op.
    /// </summary>
    void Acknowledge(IReadOnlyList<ObservationEventId> eventIds, string acknowledgedBy);

    /// <summary>Returns true if the event has been acknowledged in this session.</summary>
    bool IsAcknowledged(ObservationEventId eventId);
}
