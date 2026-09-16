namespace PoWatch.Shared.Models;

// ─────────────────────────────────────────────────────────────────────────────
// Shared contracts for the four new analytics features (heatmap, notification
// bundling, standby / wake-on-motion, pattern comparison). All types here are
// pure DTOs — no behaviour — so they can ride the source-generated JSON context
// in PoWatch.Client/Services/PoWatchJsonContext.cs alongside every other type
// that crosses the BFF boundary. None of these types need to be serialised
// from the server today; they are produced locally on the client from
// already-fetched DTOs. Live here so future server endpoints can return them
// without a second round of contract changes.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Per-bucket event counts across the 24 hours of a single local day. Index = hour-of-day,
/// Value = number of observation events that occurred in that hour. Three significance tiers
/// are kept separately so the heatmap can render them as three intensities, not as a single
/// flat count that hides routine from notable activity.
/// </summary>
public sealed class DailyActivityBucketsDto
{
    /// <summary>Hour 0..23 → count of routine observations in that hour.</summary>
    public IReadOnlyList<int> Routine { get; init; } = new int[24];
    /// <summary>Hour 0..23 → count of notable (significant, non-outlier) observations in that hour.</summary>
    public IReadOnlyList<int> Notable { get; init; } = new int[24];
    /// <summary>Hour 0..23 → count of clinical outliers in that hour.</summary>
    public IReadOnlyList<int> Urgent { get; init; } = new int[24];
    /// <summary>The local date the buckets describe (used to label the strip "Tue 14 May").</summary>
    public DateOnly Date { get; init; }
    /// <summary>Total observations across every tier — convenience for the UI label.</summary>
    public int TotalEvents { get; init; }
}



/// <summary>
/// The standby controller's most recent state. Mirrors the JS-side watchdog's view so the
/// UI can show "Awake · watching" / "Standby · waiting for motion" without polling the worker.
/// </summary>
public sealed class StandbyStatusDto
{
    /// <summary>"awake" — inference is running; "standby" — model is unloaded, watching for motion only.</summary>
    public string Mode { get; init; } = "awake";
    /// <summary>True when the feature flag / runtime preference has standby enabled.</summary>
    public bool Enabled { get; init; }
    /// <summary>Seconds since the last frame-changed motion event (infinity when no motion yet).</summary>
    public double SecondsSinceMotion { get; init; }
    /// <summary>Threshold in seconds at which the controller transitions awake → standby.</summary>
    public int IdleThresholdSeconds { get; init; }
    /// <summary>True if the wake-on-motion probe is currently sampling the camera.</summary>
    public bool MotionProbeActive { get; init; }
    /// <summary>UTC instant of the most recent state transition; used as the timer anchor.</summary>
    public DateTimeOffset? StateChangedAtUtc { get; init; }
}
