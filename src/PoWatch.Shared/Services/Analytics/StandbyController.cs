using PoWatch.Shared.Models;

namespace PoWatch.Shared.Services.Analytics;

/// <summary>
/// Pure (No-I/O) state machine that decides when the inference loop should pause and let a
/// cheap motion probe watch the camera instead. Keeps the GPU/CPU asleep during quiet hours
/// — the single largest battery/heat win for a wall-mounted tablet running PoWatch as a
/// 24/7 kiosk.
/// </summary>
/// <remarks>
/// The controller is the brain; the JS-Interop wrapper around <c>powatchInference.probeMotion</c>
/// is the eye. The split is deliberate: the state machine is unit-testable in C# (where the
/// timing policy lives), and the camera-pixel probing stays in JS (where the canvas API is).
/// </remarks>
public sealed class StandbyController
{
    private readonly TimeSpan _idleThreshold;
    private readonly Func<DateTimeOffset> _clock;

    /// <summary>The most recent motion probe timestamp — "the last time the picture changed".</summary>
    private DateTimeOffset _lastMotionAt;

    /// <summary>The current mode. Always starts in "awake" so a freshly-initialised kiosk
    /// is observable on the very first frame instead of waiting one threshold period.</summary>
    public string Mode { get; private set; } = "awake";

    /// <summary>UTC timestamp of the most recent mode transition; surfaced in the UI.</summary>
    public DateTimeOffset? StateChangedAtUtc { get; private set; }

    /// <summary>The total number of awake → standby transitions this session. Used by the
    /// System page so an operator can verify the kiosk is actually entering standby overnight.</summary>
    public int StandbyTransitions { get; private set; }

    /// <summary>The total number of standby → awake transitions this session.</summary>
    public int WakeTransitions { get; private set; }

    /// <summary>
    /// Build a controller with the given idle threshold. The default of 15 minutes matches the
    /// overnight-quiet-house target — long enough to skip a brief lull, short enough to actually
    /// unload the model before the laptop falls asleep.
    /// </summary>
    /// <param name="idleThresholdSeconds">Seconds of no motion before going to standby.</param>
    /// <param name="clock">Injectable clock for tests.</param>
    public StandbyController(int idleThresholdSeconds = 900, Func<DateTimeOffset>? clock = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(idleThresholdSeconds, 10);
        _idleThreshold = TimeSpan.FromSeconds(idleThresholdSeconds);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _lastMotionAt = _clock();
        StateChangedAtUtc = _lastMotionAt;
    }

    /// <summary>Threshold in seconds — used by the UI to render "Standby after 15 min of stillness".</summary>
    public int IdleThresholdSeconds => (int)_idleThreshold.TotalSeconds;

    /// <summary>True if standby mode is allowed at all (feature flag from the runtime).</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Signal that motion was just detected. Resets the idle timer and may wake the model.
    /// </summary>
    public void RecordMotion(DateTimeOffset? at = null)
    {
        var now = at ?? _clock();
        var wasStill = (now - _lastMotionAt) > _idleThreshold;
        _lastMotionAt = now;
        if (Mode == "standby" && wasStill)
        {
            Mode = "awake";
            StateChangedAtUtc = now;
            WakeTransitions++;
        }
    }

    /// <summary>
    /// Tick the state machine. Returns the mode the controller should be in *now* —
    /// the JS caller must unload/reload the model whenever this returns a value different
    /// from the previous tick.
    /// </summary>
    public string Tick(DateTimeOffset? now = null)
    {
        var at = now ?? _clock();
        if (Mode == "awake" && Enabled && (at - _lastMotionAt) > _idleThreshold)
        {
            Mode = "standby";
            StateChangedAtUtc = at;
            StandbyTransitions++;
        }
        return Mode;
    }

    /// <summary>Seconds elapsed since the last motion event, for the UI status line.</summary>
    public double SecondsSinceMotion(DateTimeOffset? now = null)
    {
        var at = now ?? _clock();
        return Math.Max(0, (at - _lastMotionAt).TotalSeconds);
    }

    /// <summary>Snapshot the current state for the renderer / Status page.</summary>
    public StandbyStatusDto Snapshot(bool motionProbeActive)
    {
        return new StandbyStatusDto
        {
            Mode = Mode,
            Enabled = Enabled,
            SecondsSinceMotion = SecondsSinceMotion(),
            IdleThresholdSeconds = IdleThresholdSeconds,
            MotionProbeActive = motionProbeActive,
            StateChangedAtUtc = StateChangedAtUtc
        };
    }
}
