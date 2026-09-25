namespace PoWatch.Domain.Services;

/// <summary>How today's value of a metric compares with its usual daily values.</summary>
public sealed record Anomaly(string Metric, double Today, double Mean, double StdDev, double? Z, bool Flagged);

/// <summary>Stat family C — "today vs. your usual" and trends.</summary>
public static class AnomalyMath
{
    /// <summary>|z| at or above this is called out as unusual.</summary>
    public const double FlagThreshold = 2;

    /// <summary>
    /// Pearson correlation between today's hourly curve and the usual one: 1 is a textbook day, 0 is
    /// unrelated, negative is upside down. Null when either curve is flat, where correlation is undefined.
    /// </summary>
    public static double? RhythmScore(IReadOnlyList<double> today, IReadOnlyList<double> usual)
    {
        ArgumentNullException.ThrowIfNull(today);
        ArgumentNullException.ThrowIfNull(usual);
        var n = Math.Min(today.Count, usual.Count);
        if (n < 2) return null;

        double meanA = 0, meanB = 0;
        for (var i = 0; i < n; i++) { meanA += today[i]; meanB += usual[i]; }
        meanA /= n;
        meanB /= n;

        double cov = 0, varA = 0, varB = 0;
        for (var i = 0; i < n; i++)
        {
            var da = today[i] - meanA;
            var db = usual[i] - meanB;
            cov += da * db;
            varA += da * da;
            varB += db * db;
        }

        return varA <= 0 || varB <= 0 ? null : cov / Math.Sqrt(varA * varB);
    }

    /// <summary>Z-score of <paramref name="today"/> against the sample of usual daily values.</summary>
    public static Anomaly Compare(string metric, double today, IReadOnlyList<double> baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        if (baseline.Count == 0) return new Anomaly(metric, today, 0, 0, null, false);

        var mean = baseline.Average();
        var sd = baseline.Count < 2
            ? 0
            : Math.Sqrt(baseline.Sum(v => (v - mean) * (v - mean)) / (baseline.Count - 1));
        double? z = sd > 0 ? (today - mean) / sd : null;

        return new Anomaly(metric, today, mean, sd, z, z is { } value && Math.Abs(value) >= FlagThreshold);
    }

    /// <summary>Least-squares slope of consecutive daily values, in units per day.</summary>
    public static double TrendSlope(IReadOnlyList<double> dailyValues)
    {
        ArgumentNullException.ThrowIfNull(dailyValues);
        var n = dailyValues.Count;
        if (n < 2) return 0;

        var meanX = (n - 1) / 2.0;
        var meanY = dailyValues.Average();
        double num = 0, den = 0;
        for (var i = 0; i < n; i++)
        {
            num += (i - meanX) * (dailyValues[i] - meanY);
            den += (i - meanX) * (i - meanX);
        }

        return num / den;
    }
}
