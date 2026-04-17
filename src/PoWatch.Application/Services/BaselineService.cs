using Microsoft.Extensions.Logging;
using PoWatch.Application.Contracts;
using PoWatch.Shared.Models;

namespace PoWatch.Application.Services;

/// <summary>
/// Computes a 7-day behavioral baseline and drift score for a subject.
/// Drift score: (1 − cosine_similarity(baseline_vector, today_vector)) × 100.
/// A score near 0 means today looks like an average day; near 100 means completely atypical.
/// </summary>
public sealed class BaselineService(
    IObservationRepository observationRepository,
    ISubjectRepository subjectRepository,
    ILogger<BaselineService> logger)
{
    private const int DefaultBaselineDays = 7;

    public async Task<SubjectBaselineDto> GetBaselineAsync(
        string subjectId,
        CancellationToken cancellationToken,
        int baselineDays = DefaultBaselineDays)
    {
        logger.LogInformation(
            "Computing behavioral baseline. SubjectId={SubjectId} BaselineDays={BaselineDays}",
            subjectId,
            baselineDays);

        var subject = await subjectRepository.GetByIdAsync(subjectId, cancellationToken);
        if (subject is null)
        {
            logger.LogWarning("Subject not found for baseline computation. SubjectId={SubjectId}", subjectId);
            throw new InvalidOperationException($"Subject '{subjectId}' was not found.");
        }

        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var historyFrom = today.AddDays(-(baselineDays));
        var historyTo = today.AddDays(-1); // exclude today from baseline

        var historicalEvents = await observationRepository.GetByDateRangeAsync(historyFrom, historyTo, cancellationToken);
        var todayEvents = await observationRepository.GetByDateAsync(today, cancellationToken);

        var localOffset = TimeZoneInfo.Local.GetUtcOffset(today.ToDateTime(TimeOnly.MinValue));

        var subjectHistorical = historicalEvents
            .Where(e => string.Equals(e.SubjectId, subjectId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var subjectToday = todayEvents
            .Where(e => string.Equals(e.SubjectId, subjectId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var baselineVector = BuildHourlyVector(subjectHistorical, localOffset);
        var todayVector = BuildHourlyVector(subjectToday, localOffset);
        var driftScore = ComputeDriftScore(baselineVector, todayVector);
        var driftLabel = driftScore switch
        {
            < 10 => "Normal",
            < 30 => "Slight variation",
            < 60 => "Moderate drift",
            < 80 => "High drift",
            _ => "Extreme deviation"
        };

        logger.LogInformation(
            "Baseline computed. SubjectId={SubjectId} HistoricalEvents={Historical} TodayEvents={Today} DriftScore={Drift:F1} DriftLabel={Label}",
            subjectId,
            subjectHistorical.Count,
            subjectToday.Count,
            driftScore,
            driftLabel);

        return new SubjectBaselineDto
        {
            SubjectId = subject.SubjectId,
            DisplayName = subject.DisplayName,
            ComputedForDate = today,
            BaselineDays = baselineDays,
            HourlyBaselineVector = baselineVector,
            HourlyTodayVector = todayVector,
            DriftScore = Math.Round(driftScore, 1),
            DriftLabel = driftLabel,
            GeneratedAtUtc = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Builds a 24-element vector where each element is the fraction of events in that local hour
    /// relative to the total number of events (density normalised to 0–1 per hour).
    /// Returns a zero vector if there are no events.
    /// </summary>
    private static double[] BuildHourlyVector(IReadOnlyList<Domain.Models.ObservationEvent> events, TimeSpan localOffset)
    {
        var vector = new double[24];
        if (events.Count == 0) return vector;

        foreach (var e in events)
        {
            var localHour = (int)((e.ObservedAtUtc + localOffset).TimeOfDay.TotalHours) % 24;
            vector[localHour]++;
        }

        // Normalise so each element is events_in_hour / total_events
        var total = (double)events.Count;
        for (var i = 0; i < 24; i++)
            vector[i] /= total;

        return vector;
    }

    /// <summary>Returns (1 − cosine_similarity) × 100. Range: 0 (identical) to 100 (orthogonal).</summary>
    private static double ComputeDriftScore(double[] baseline, double[] today)
    {
        var dot = 0.0;
        var magA = 0.0;
        var magB = 0.0;

        for (var i = 0; i < 24; i++)
        {
            dot += baseline[i] * today[i];
            magA += baseline[i] * baseline[i];
            magB += today[i] * today[i];
        }

        if (magA == 0 || magB == 0)
        {
            // If either vector is all-zeros (no data), treat as maximum drift
            return (magA == 0 && magB == 0) ? 0 : 100;
        }

        var cosine = dot / (Math.Sqrt(magA) * Math.Sqrt(magB));
        // Clamp to [-1,1] to guard against floating-point rounding
        cosine = Math.Max(-1.0, Math.Min(1.0, cosine));
        return (1.0 - cosine) * 100.0;
    }
}
