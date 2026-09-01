using PoWatch.Shared.Models;

namespace PoWatch.Shared.Services.Analytics;

/// <summary>
/// Pure (No-I/O) comparator that takes today's hourly activity vector against the rolling
/// 7-day baseline (both already on the wire from <c>GET /api/identity/subjects/{id}/baseline</c>)
/// and produces a caregiver-readable summary. This is the engine behind the "Pattern
/// comparison" panel — the actual comparison surface lives on the History / People pages.
/// </summary>
/// <remarks>
/// The comparator never recomputes drift — the server already returns a drift score via
/// DriftRadarService, and that score is what this class interprets. The job here is purely
/// to translate "0..100 + label" into one plain sentence a caregiver can read in a glance.
/// </remarks>
public static class PatternComparator
{
    /// <summary>
    /// Compare today's vector against the baseline window. The baseline vector and today
    /// vector MUST both have length 24; the function pads/truncates defensively rather than
    /// throwing — the caller may have empty vectors for a brand-new subject and that is a
    /// "no data" condition, not a crash.
    /// </summary>
    public static PatternComparisonDto Compare(SubjectBaselineDto baseline)
    {
        var baselineVector = Normalise24(baseline.HourlyBaselineVector);
        var todayVector = Normalise24(baseline.HourlyTodayVector);

        var totalBaseline = Sum(baselineVector);
        var totalToday = Sum(todayVector);
        var direction = ClassifyDirection(totalBaseline, totalToday);
        var summary = BuildSummary(baseline.DriftLabel, baseline.DriftScore, totalBaseline, totalToday);

        return new PatternComparisonDto
        {
            SubjectId = baseline.SubjectId,
            DisplayName = baseline.DisplayName,
            Baseline = baselineVector,
            Today = todayVector,
            DriftScore = baseline.DriftScore,
            DriftLabel = baseline.DriftLabel,
            Direction = direction,
            Summary = summary
        };
    }

    private static IReadOnlyList<double> Normalise24(IReadOnlyList<double>? input)
    {
        var output = new double[24];
        if (input is null) return output;
        var max = 0d;
        for (var i = 0; i < Math.Min(24, input.Count); i++)
        {
            output[i] = input[i];
            if (input[i] > max) max = input[i];
        }
        if (max > 1d)
        {
            // Defensive: the server should already normalise, but if it ever returns a raw
            // histogram (counts) we still want a 0..1 curve so the bar heights render right.
            for (var i = 0; i < 24; i++) output[i] /= max;
        }
        return output;
    }

    private static double Sum(IReadOnlyList<double> v)
    {
        var s = 0d;
        for (var i = 0; i < v.Count; i++) s += v[i];
        return s;
    }

    private static string ClassifyDirection(double baseline, double today)
    {
        if (baseline < 0.001) return "matching";
        var ratio = today / baseline;
        if (ratio >= 1.25) return "higher";
        if (ratio <= 0.75) return "lower";
        return "matching";
    }

    private static string BuildSummary(string driftLabel, double driftScore, double baselineTotal, double todayTotal)
    {
        if (baselineTotal < 0.001)
        {
            return "Not enough history yet — keep watching and a baseline will form.";
        }

        var ratio = todayTotal / baselineTotal;
        var rounded = Math.Round(ratio * 100d, 0);
        return driftLabel switch
        {
            "Very different" => $"Today is very different from usual ({rounded}% of typical activity).",
            "Off-pattern" => $"Off-pattern today ({rounded}% of typical activity).",
            "Slightly off" => $"Slightly different today ({rounded}% of typical activity).",
            _ => $"Today matches the usual pattern ({rounded}% of typical activity)."
        };
    }
}
