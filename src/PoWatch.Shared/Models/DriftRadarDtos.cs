namespace PoWatch.Shared.Models;

/// <summary>Per-subject drift risk status for the Drift Radar feature.</summary>
public sealed class SubjectDriftStatusDto
{
    public string SubjectId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Drift score 0–100. Higher = greater deviation from baseline.</summary>
    public double DriftScore { get; init; }

    /// <summary>Normal | Slight Variation | Moderate Drift | High Drift | Extreme Deviation | Insufficient Data</summary>
    public string DriftLabel { get; init; } = string.Empty;

    public int TodayEventCount { get; init; }

    /// <summary>24-element hourly density vector for the baseline window. Index 0 = midnight local.</summary>
    public IReadOnlyList<double> HourlyBaselineVector { get; init; } = new double[24];

    /// <summary>24-element hourly density vector for today only.</summary>
    public IReadOnlyList<double> HourlyTodayVector { get; init; } = new double[24];

    /// <summary>Human-readable explanations of the top contributing drift factors.</summary>
    public IReadOnlyList<DriftInsightDto> Insights { get; init; } = [];

    public DateTimeOffset ComputedAtUtc { get; init; }
}

/// <summary>A single insight explaining a specific drift factor for a subject.</summary>
public sealed class DriftInsightDto
{
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    /// <summary>low | medium | high</summary>
    public string Severity { get; init; } = string.Empty;
}
