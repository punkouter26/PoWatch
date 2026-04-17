namespace PoWatch.Application.Options;

/// <summary>Configures drift scoring thresholds and baseline window for the Drift Radar feature.</summary>
public sealed class DriftRadarOptions
{
    /// <summary>
    /// Minimum number of today's events required to compute a meaningful drift score.
    /// Subjects with fewer events receive the "Insufficient Data" label.
    /// </summary>
    public int MinEventsForDrift { get; init; } = 3;

    /// <summary>Drift score at or above which the label becomes "High Drift".</summary>
    public double HighDriftThreshold { get; init; } = 60.0;

    /// <summary>Drift score at or above which the label becomes "Moderate Drift".</summary>
    public double ModerateDriftThreshold { get; init; } = 30.0;

    /// <summary>Drift score at or above which the label becomes "Slight Variation".</summary>
    public double SlightDriftThreshold { get; init; } = 10.0;

    /// <summary>Number of historical days included in the baseline vector window.</summary>
    public int BaselineDays { get; init; } = 7;

    /// <summary>Maximum number of drift insights to return per subject.</summary>
    public int MaxInsights { get; init; } = 4;
}
