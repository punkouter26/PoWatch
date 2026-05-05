using Microsoft.Extensions.Logging;
using PoWatch.Application.Contracts;
using PoWatch.Domain.Models;
using PoWatch.Shared.Models;

namespace PoWatch.Application.Services;

/// <summary>
/// Builds the <see cref="ShiftHandoffReportDto"/> data model for a given date and shift window.
/// Rendering to PDF/HTML is handled by the API layer (see PoWatch.Api).
/// </summary>
public sealed class ReportService(
    IObservationRepository observationRepository,
    ILogger<ReportService> logger)
{
    public async Task<ShiftHandoffReportDto> BuildHandoffReportAsync(
        DateOnly date,
        ShiftWindow shiftWindow,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Building handoff report. Date={Date} ShiftWindow={ShiftWindow}",
            date,
            shiftWindow);

        var allEvents = await observationRepository.GetByDateAsync(date, cancellationToken);
        var filtered = FilterByShift(allEvents, date, shiftWindow).ToList();

        var significantEvents = filtered
            .Where(e => e.IsSignificant && !e.IsClinicalOutlier)
            .OrderBy(e => e.ObservedAtUtc)
            .ToList();

        var outlierEvents = filtered
            .Where(e => e.IsClinicalOutlier)
            .OrderBy(e => e.ObservedAtUtc)
            .ToList();

        var primarySubject = filtered
            .GroupBy(e => e.SubjectDisplayName)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault() ?? "No subjects identified";

        var dominantActivity = filtered
            .GroupBy(e => e.Activity)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault() ?? "No activity recorded";

        var narrative = filtered.Count == 0
            ? $"No observations recorded for the {shiftWindow} shift on {date:yyyy-MM-dd}."
            : $"The {shiftWindow} shift recorded {filtered.Count} events. Primary subject: {primarySubject}. " +
              $"Dominant activity: {dominantActivity}. " +
              $"Significant events: {significantEvents.Count}. " +
              $"Clinical outliers flagged: {outlierEvents.Count}.";

        logger.LogInformation(
            "Handoff report built. Date={Date} ShiftWindow={ShiftWindow} TotalEvents={Total} Significant={Significant} Outliers={Outliers}",
            date,
            shiftWindow,
            filtered.Count,
            significantEvents.Count,
            outlierEvents.Count);

        return new ShiftHandoffReportDto
        {
            Date = date,
            ShiftWindow = shiftWindow,
            PrimarySubject = primarySubject,
            DominantActivity = dominantActivity,
            TotalEvents = filtered.Count,
            OutlierCount = outlierEvents.Count,
            SignificantCount = significantEvents.Count,
            ClinicalNarrative = narrative,
            SignificantEvents = MapEvents(significantEvents),
            OutlierEvents = MapEvents(outlierEvents),
            GeneratedAtUtc = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Filters events by shift window using local time.
    /// 
    /// Shift Window Boundaries (local time):
    /// - Morning:   [06:00, 14:00) — includes 06:00, excludes 14:00
    /// - Afternoon: [14:00, 22:00) — includes 14:00, excludes 22:00  
    /// - Night:     [22:00, 06:00) — includes 22:00-23:59 and 00:00-05:59
    /// - FullDay:   All events (no filtering)
    /// 
    /// Note: At exactly 06:00, events belong to Morning (not Night).
    /// At exactly 14:00, events belong to Afternoon (not Morning).
    /// At exactly 22:00, events belong to Night (not Afternoon).
    /// </summary>
    private static IEnumerable<ObservationEvent> FilterByShift(
        IReadOnlyList<ObservationEvent> events,
        DateOnly date,
        ShiftWindow window)
    {
        if (window == ShiftWindow.FullDay)
            return events;

        var localOffset = TimeZoneInfo.Local.GetUtcOffset(date.ToDateTime(TimeOnly.MinValue));

        return events.Where(e =>
        {
            // Calculate local hour from UTC timestamp
            var localHour = (int)((e.ObservedAtUtc + localOffset).TimeOfDay.TotalHours);

            // Use half-open intervals [start, end) for Morning/Afternoon
            // and compound condition for Night [22,24) ∪ [0,6)
            return window switch
            {
                // Half-open interval: includes start hour, excludes end hour
                ShiftWindow.Morning => localHour >= 6 && localHour < 14,     // [06:00, 14:00)
                ShiftWindow.Afternoon => localHour >= 14 && localHour < 22,  // [14:00, 22:00)

                // Night uses union of two ranges: [22:00-23:59] OR [00:00-05:59]
                // Equivalent to: localHour >= 22 OR localHour < 6
                ShiftWindow.Night => localHour >= 22 || localHour < 6,

                _ => true
            };
        });
    }

    private static IReadOnlyList<ObservationEventDto> MapEvents(IEnumerable<ObservationEvent> events) =>
        events.Select(e => new ObservationEventDto
        {
            Id = e.Id,
            ObservedAtUtc = e.ObservedAtUtc,
            SubjectId = e.SubjectId,
            SubjectDisplayName = e.SubjectDisplayName,
            Activity = e.Activity,
            ClinicalDescription = e.ClinicalDescription,
            IsSignificant = e.IsSignificant,
            SignificantReason = e.SignificantReason,
            IsClinicalOutlier = e.IsClinicalOutlier,
            ImageReference = e.ImageReference
        }).ToList();
}
