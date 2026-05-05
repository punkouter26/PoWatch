using PoWatch.Domain.Models;

namespace PoWatch.Application.Services;

/// <summary>
/// Shared drift math used by both <see cref="BaselineService"/> (single-subject) and
/// <see cref="DriftRadarService"/> (bulk multi-subject). Single source of truth for
/// hourly-vector construction and cosine-similarity drift scoring.
/// </summary>
public static class DriftMath
{
    // Default threshold constants for drift classification
    public const double SlightDriftThreshold = 10;
    public const double ModerateDriftThreshold = 30;
    public const double HighDriftThreshold = 60;
    public const double ExtremeDriftThreshold = 80;

    /// <summary>
    /// Builds a 24-element density vector where each element is the fraction of events
    /// in that local hour relative to the total event count (0–1 per slot).
    /// Returns a zero vector when there are no events.
    /// </summary>
    public static double[] BuildHourlyVector(IReadOnlyList<ObservationEvent> events, TimeSpan localOffset)
    {
        var vector = new double[24];
        if (events.Count == 0) return vector;

        foreach (var e in events)
        {
            var localHour = (int)((e.ObservedAtUtc + localOffset).TimeOfDay.TotalHours) % 24;
            vector[localHour]++;
        }

        var total = (double)events.Count;
        for (var i = 0; i < 24; i++)
            vector[i] /= total;

        return vector;
    }

    /// <summary>
    /// Returns (1 − cosine_similarity) × 100. Range: 0 (identical pattern) → 100 (orthogonal).
    /// Both zero vectors → 0. One zero vector → 100 (maximum drift).
    /// </summary>
    public static double ComputeDriftScore(double[] baseline, double[] today)
    {
        double dot = 0, magA = 0, magB = 0;
        for (var i = 0; i < 24; i++)
        {
            dot += baseline[i] * today[i];
            magA += baseline[i] * baseline[i];
            magB += today[i] * today[i];
        }

        if (magA == 0 || magB == 0)
            return (magA == 0 && magB == 0) ? 0 : 100;

        var cosine = Math.Max(-1.0, Math.Min(1.0, dot / (Math.Sqrt(magA) * Math.Sqrt(magB))));
        return (1.0 - cosine) * 100.0;
    }

    /// <summary>
    /// Classifies a drift score into a human-readable label.
    /// </summary>
    public static string ClassifyDrift(double score) => score switch
    {
        >= ExtremeDriftThreshold => "Extreme Deviation",
        >= HighDriftThreshold => "High Drift",
        >= ModerateDriftThreshold => "Moderate Drift",
        >= SlightDriftThreshold => "Slight Variation",
        _ => "Normal"
    };

    /// <summary>
    /// Classifies a drift score with custom thresholds from options.
    /// </summary>
    public static string ClassifyDrift(double score, double highThreshold, double moderateThreshold, double slightThreshold) =>
        score switch
        {
            >= ExtremeDriftThreshold => "Extreme Deviation",
            _ when score >= highThreshold => "High Drift",
            _ when score >= moderateThreshold => "Moderate Drift",
            _ when score >= slightThreshold => "Slight Variation",
            _ => "Normal"
        };
}
