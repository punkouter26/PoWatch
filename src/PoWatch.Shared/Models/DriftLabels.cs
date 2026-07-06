namespace PoWatch.Shared.Models;

/// <summary>
/// Single source of truth for drift-status labels. Producer (<c>DriftMath.ClassifyDrift</c>,
/// <c>DriftRadarService</c>) and all consumers (the hand-off summarizers) reference these constants
/// instead of duplicating raw literals, so renaming a label is a compile-time-safe change in one place
/// rather than a silent break of clinical alert filtering scattered across layers.
/// </summary>
public static class DriftLabels
{
    public const string Normal = "Normal";
    public const string Slight = "Slight Variation";
    public const string Moderate = "Moderate Drift";
    public const string High = "High Drift";
    public const string Extreme = "Extreme Deviation";
    public const string InsufficientData = "Insufficient Data";
}
