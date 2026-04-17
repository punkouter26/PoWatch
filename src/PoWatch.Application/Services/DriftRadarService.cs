using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PoWatch.Application.Contracts;
using PoWatch.Application.Options;
using PoWatch.Domain.Models;
using PoWatch.Shared.Models;

namespace PoWatch.Application.Services;

/// <summary>
/// Computes per-subject drift status for all known subjects using bulk event loads.
/// Reuses the cosine-similarity drift math from <see cref="BaselineService"/> but
/// processes all subjects in a single pass to avoid N+1 repository queries.
/// </summary>
public sealed class DriftRadarService(
    ISubjectRepository subjectRepository,
    IObservationRepository observationRepository,
    IOptions<DriftRadarOptions> options,
    ILogger<DriftRadarService> logger)
{
    public async Task<IReadOnlyList<SubjectDriftStatusDto>> GetDriftStatusAsync(CancellationToken cancellationToken)
    {
        var profiles = await subjectRepository.GetAllAsync(cancellationToken);
        if (profiles.Count == 0)
        {
            logger.LogDebug("Drift Radar: no subjects found.");
            return [];
        }

        var today = DateOnly.FromDateTime(DateTimeOffset.Now.LocalDateTime);
        var historyFrom = today.AddDays(-options.Value.BaselineDays);
        var historyTo = today.AddDays(-1);

        // Single bulk load — avoids N repository calls for N subjects
        var historicalEvents = await observationRepository.GetByDateRangeAsync(historyFrom, historyTo, cancellationToken);
        var todayEvents = await observationRepository.GetByDateAsync(today, cancellationToken);

        var localOffset = TimeZoneInfo.Local.GetUtcOffset(today.ToDateTime(TimeOnly.MinValue));
        var result = new List<SubjectDriftStatusDto>(profiles.Count);

        foreach (var profile in profiles)
        {
            var subjectHistorical = historicalEvents
                .Where(e => string.Equals(e.SubjectId, profile.SubjectId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var subjectToday = todayEvents
                .Where(e => string.Equals(e.SubjectId, profile.SubjectId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (subjectToday.Count < options.Value.MinEventsForDrift)
            {
                result.Add(new SubjectDriftStatusDto
                {
                    SubjectId = profile.SubjectId,
                    DisplayName = profile.DisplayName,
                    DriftScore = 0,
                    DriftLabel = "Insufficient Data",
                    TodayEventCount = subjectToday.Count,
                    HourlyBaselineVector = new double[24],
                    HourlyTodayVector = new double[24],
                    Insights = [],
                    ComputedAtUtc = DateTimeOffset.UtcNow
                });
                continue;
            }

            var baselineVector = BuildHourlyVector(subjectHistorical, localOffset);
            var todayVector = BuildHourlyVector(subjectToday, localOffset);
            var driftScore = ComputeDriftScore(baselineVector, todayVector);
            var driftLabel = ClassifyDrift(driftScore);
            var insights = BuildInsights(baselineVector, todayVector, driftScore, subjectToday, subjectHistorical);

            logger.LogDebug(
                "Drift Radar: SubjectId={SubjectId} DriftScore={DriftScore:F1} Label={Label} TodayEvents={Today} HistoricalEvents={Historical}",
                profile.SubjectId, driftScore, driftLabel, subjectToday.Count, subjectHistorical.Count);

            result.Add(new SubjectDriftStatusDto
            {
                SubjectId = profile.SubjectId,
                DisplayName = profile.DisplayName,
                DriftScore = Math.Round(driftScore, 1),
                DriftLabel = driftLabel,
                TodayEventCount = subjectToday.Count,
                HourlyBaselineVector = baselineVector,
                HourlyTodayVector = todayVector,
                Insights = insights,
                ComputedAtUtc = DateTimeOffset.UtcNow
            });
        }

        return result.OrderByDescending(s => s.DriftScore).ToList();
    }

    private string ClassifyDrift(double score) => score switch
    {
        >= 80 => "Extreme Deviation",
        var s when s >= options.Value.HighDriftThreshold => "High Drift",
        var s when s >= options.Value.ModerateDriftThreshold => "Moderate Drift",
        var s when s >= options.Value.SlightDriftThreshold => "Slight Variation",
        _ => "Normal"
    };

    private IReadOnlyList<DriftInsightDto> BuildInsights(
        double[] baseline,
        double[] today,
        double driftScore,
        IReadOnlyList<ObservationEvent> todayEvents,
        IReadOnlyList<ObservationEvent> historicalEvents)
    {
        var insights = new List<DriftInsightDto>();

        // Insight 1: Peak activity window shift
        var baselinePeak = Array.IndexOf(baseline, baseline.Max());
        var todayPeak = Array.IndexOf(today, today.Max());
        var peakShift = Math.Abs(todayPeak - baselinePeak);
        if (peakShift >= 3)
        {
            insights.Add(new DriftInsightDto
            {
                Title = "Peak activity window shifted",
                Description = $"Peak activity has moved from {baselinePeak:D2}:00 (baseline) to {todayPeak:D2}:00 today — a shift of {peakShift} hours.",
                Severity = peakShift >= 6 ? "high" : "medium"
            });
        }

        // Insight 2: Nighttime activity increase (22:00–05:00)
        var nightHours = new[] { 22, 23, 0, 1, 2, 3, 4, 5 };
        var baselineNight = nightHours.Sum(h => baseline[h]);
        var todayNight = nightHours.Sum(h => today[h]);
        if (baselineNight > 0 && todayNight > baselineNight * 1.5)
        {
            insights.Add(new DriftInsightDto
            {
                Title = "Elevated nighttime activity",
                Description = $"Nighttime movement is {todayNight / baselineNight:F1}× higher than the {options.Value.BaselineDays}-day baseline.",
                Severity = todayNight > baselineNight * 2.5 ? "high" : "medium"
            });
        }
        else if (baselineNight > 0.1 && todayNight < baselineNight * 0.3)
        {
            insights.Add(new DriftInsightDto
            {
                Title = "Reduced nighttime activity",
                Description = $"Nighttime movement is significantly lower than the {options.Value.BaselineDays}-day average.",
                Severity = "low"
            });
        }

        // Insight 3: Outlier rate spike
        var outlierCount = todayEvents.Count(e => e.IsClinicalOutlier);
        var outlierRate = todayEvents.Count > 0 ? (double)outlierCount / todayEvents.Count : 0;
        if (outlierRate >= 0.15)
        {
            insights.Add(new DriftInsightDto
            {
                Title = "Elevated clinical outlier rate",
                Description = $"{outlierCount} of {todayEvents.Count} events ({outlierRate:P0}) were flagged as clinical outliers.",
                Severity = outlierRate >= 0.3 ? "high" : "medium"
            });
        }

        // Insight 4: Event volume deviation vs baseline average
        if (historicalEvents.Count > 0)
        {
            var baselineAvgPerDay = (double)historicalEvents.Count / options.Value.BaselineDays;
            if (baselineAvgPerDay > 0)
            {
                var volumeRatio = todayEvents.Count / baselineAvgPerDay;
                if (volumeRatio < 0.4)
                {
                    insights.Add(new DriftInsightDto
                    {
                        Title = "Much lower activity than usual",
                        Description = $"Today has {todayEvents.Count} events vs. a daily average of {baselineAvgPerDay:F0} — only {volumeRatio:P0} of typical volume.",
                        Severity = volumeRatio < 0.2 ? "high" : "medium"
                    });
                }
                else if (volumeRatio > 2.5)
                {
                    insights.Add(new DriftInsightDto
                    {
                        Title = "Unusually high activity volume",
                        Description = $"Today has {todayEvents.Count} events vs. a daily average of {baselineAvgPerDay:F0} — {volumeRatio:F1}× the typical volume.",
                        Severity = volumeRatio > 4 ? "high" : "medium"
                    });
                }
            }
        }

        return insights.Take(options.Value.MaxInsights).ToList();
    }

    private static double[] BuildHourlyVector(IReadOnlyList<ObservationEvent> events, TimeSpan localOffset)
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

    private static double ComputeDriftScore(double[] baseline, double[] today)
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
}
