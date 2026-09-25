using PoWatch.Shared.Services.Cadence;

namespace PoWatch.Shared.Services.Sensing;

/// <summary>
/// Decides when the vision model runs next. The pixel layer's motion is smoothed and mapped through
/// <see cref="AdaptiveCadence"/>: a still room slows captions to save power, a busy one speeds them
/// up so the model sees fresh material.
/// </summary>
public sealed class VlmScheduler(TimeProvider time, int baseIntervalSeconds = 15)
{
    /// <summary>Pixel motion at or above this counts as "as busy as it gets" for the cadence.</summary>
    public const double BusyMotion = 0.15;

    private const double Smoothing = 0.2;

    private DateTimeOffset? _lastRunUtc;
    private double _motion;

    public TimeSpan Interval =>
        TimeSpan.FromSeconds(AdaptiveCadence.ScoreToIntervalSeconds(Math.Min(1, _motion / BusyMotion), baseIntervalSeconds));

    public bool IsDue => _lastRunUtc is not { } last || time.GetUtcNow() - last >= Interval;

    public void ObserveMotion(double motion) => _motion = (Smoothing * motion) + ((1 - Smoothing) * _motion);

    public void MarkRun() => _lastRunUtc = time.GetUtcNow();
}
