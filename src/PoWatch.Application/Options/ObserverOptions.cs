using System.ComponentModel.DataAnnotations;

namespace PoWatch.Application.Options;

public sealed class ObserverOptions
{
    [Range(1, 3600)]
    public int PollingIntervalSeconds { get; init; } = 10;

    /// <summary>
    /// How long an unnamed <c>Subject-N</c> stays eligible for reuse when an observation arrives with
    /// no subject hint. Without this, every hint-less observation minted a brand-new provisional
    /// identity, so a single person sitting in the room became a new "person" on every cycle and the
    /// People list filled with un-nameable duplicates.
    /// <para>
    /// Tune against the observation interval: it must comfortably exceed one cycle, or consecutive
    /// sightings of the same person still split. Set to 0 to restore the always-create behaviour.
    /// </para>
    /// </summary>
    [Range(0, 86400)]
    public int ProvisionalSubjectReuseWindowSeconds { get; init; } = 1800;
}
