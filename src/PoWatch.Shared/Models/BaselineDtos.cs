namespace PoWatch.Shared.Models;

/// <summary>7-day behavioral baseline and drift score for a subject.</summary>
public sealed class SubjectBaselineDto
{
    public string SubjectId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    /// <summary>Date the baseline was computed for (today).</summary>
    public DateOnly ComputedForDate { get; init; }
    /// <summary>Number of historical days included in the baseline window.</summary>
    public int BaselineDays { get; init; }
    /// <summary>
    /// Hourly activity density (0–1) for baseline period. Index 0 = midnight, index 23 = 23:00 local.
    /// </summary>
    public IReadOnlyList<double> HourlyBaselineVector { get; init; } = new double[24];
    /// <summary>
    /// Hourly activity density (0–1) for today only.
    /// </summary>
    public IReadOnlyList<double> HourlyTodayVector { get; init; } = new double[24];
    /// <summary>
    /// Drift score 0–100. Higher = greater deviation from baseline.
    /// Computed as (1 − cosine_similarity) × 100.
    /// </summary>
    public double DriftScore { get; init; }
    public string DriftLabel { get; init; } = string.Empty;
    public DateTimeOffset GeneratedAtUtc { get; init; }
}
