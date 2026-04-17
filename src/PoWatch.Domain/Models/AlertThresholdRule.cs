namespace PoWatch.Domain.Models;

/// <summary>
/// Defines a named rule that fires when a given metric exceeds a threshold
/// within a rolling time window for a single subject.
/// </summary>
public sealed class AlertThresholdRule
{
    /// <summary>Unique human-readable name surfaced in UI banners and TTS announcements.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Metric to count in the rolling window (Outlier | Significant | Any).</summary>
    public AlertMetric Metric { get; init; } = AlertMetric.Outlier;

    /// <summary>Rolling window duration in minutes.</summary>
    public int WindowMinutes { get; init; } = 10;

    /// <summary>Number of matching events within the window that triggers the alert.</summary>
    public int Threshold { get; init; } = 3;

    /// <summary>Whether the rule is currently active. Defaults to true.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Human-readable description surfaced in the UI alert banner.</summary>
    public string Description { get; init; } = string.Empty;
}

public enum AlertMetric
{
    /// <summary>Count clinical outlier events.</summary>
    Outlier,

    /// <summary>Count significant (non-outlier) events.</summary>
    Significant,

    /// <summary>Count all accepted events (significant + non-significant + outliers).</summary>
    Any
}
