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
/// A single notification event emitted by the bundling aggregator. The renderer consumes this
/// directly, never the raw ingest result, so a "noisy hour" still delivers ONE clean toast
/// instead of ten.
/// </summary>
public sealed class BundledAlertDto
{
    /// <summary>"routine" | "notable" | "urgent" — decides colour and icon.</summary>
    public string Severity { get; init; } = "routine";
    /// <summary>Heads-up line ("3 routine moments in the last 2 min").</summary>
    public string Headline { get; init; } = string.Empty;
    /// <summary>Optional secondary line with the single most important underlying reason.</summary>
    public string? Detail { get; init; }
    /// <summary>How many raw events collapsed into this bundle.</summary>
    public int Count { get; init; } = 1;
    /// <summary>UTC instant of the most recent underlying event.</summary>
    public DateTimeOffset LastEventUtc { get; init; }
    /// <summary>Subject ids of the underlying events, de-duplicated. Used by the "See details" path.</summary>
    public IReadOnlyList<string> SubjectIds { get; init; } = [];
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

/// <summary>
/// Today's hour-by-hour activity side-by-side with the rolling 7-day baseline. Drives the
/// "Pattern comparison" panel — straight derivation, no extra server call, both vectors
/// already come from GET /api/identity/subjects/{id}/baseline.
/// </summary>
public sealed class PatternComparisonDto
{
    public string SubjectId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    /// <summary>Hour 0..23 → activity density 0..1 for the baseline window.</summary>
    public IReadOnlyList<double> Baseline { get; init; } = new double[24];
    /// <summary>Hour 0..23 → activity density 0..1 for today only.</summary>
    public IReadOnlyList<double> Today { get; init; } = new double[24];
    /// <summary>0..100; the drift score the server already computed.</summary>
    public double DriftScore { get; init; }
    /// <summary>"Typical" / "Slightly off" / "Off-pattern" / "Very different".</summary>
    public string DriftLabel { get; init; } = string.Empty;
    /// <summary>Plain-language summary the caregiver actually reads.</summary>
    public string Summary { get; init; } = string.Empty;
    /// <summary>"higher" / "lower" / "matching" — the delta direction between today and baseline.</summary>
    public string Direction { get; init; } = "matching";
}
