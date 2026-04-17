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

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
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

        var baselineVector = DriftMath.BuildHourlyVector(subjectHistorical, localOffset);
        var todayVector = DriftMath.BuildHourlyVector(subjectToday, localOffset);
        var driftScore = DriftMath.ComputeDriftScore(baselineVector, todayVector);
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

}
