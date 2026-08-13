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

        // Shift boundaries are local wall-clock times, but storage is partitioned by UTC date, so the
        // window is resolved to instants first and the matching partitions read from that (ShiftClock).
        // The previous approach read one UTC partition and compared local hours inside it, which made
        // the Night shift unrepresentable: at a negative UTC offset its 00:00–06:00 half lives in the
        // next partition entirely, so those events could never be returned.
        var (windowStartUtc, windowEndUtc) = ShiftClock.WindowFor(date, shiftWindow);
        var filtered = await ShiftClock.LoadWindowAsync(observationRepository, windowStartUtc, windowEndUtc, cancellationToken);

        var significantEvents = filtered
            .Where(e => e.IsSignificant && !e.IsClinicalOutlier)
            .OrderBy(e => e.ObservedAtUtc)
            .ToList();

        var outlierEvents = filtered
            .Where(e => e.IsClinicalOutlier)
            .OrderBy(e => e.ObservedAtUtc)
            .ToList();

        // Humanized like everywhere else — a handoff brief that names "Subject-116" is asking the
        // next caregiver to translate a storage id.
        var primarySubject = SubjectDisplayNames.Humanize(
            filtered
                .GroupBy(e => e.SubjectDisplayName)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault());

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
            "Handoff report built. Date={Date} ShiftWindow={ShiftWindow} WindowStartUtc={Start} WindowEndUtc={End} TotalEvents={Total} Significant={Significant} Outliers={Outliers}",
            date,
            shiftWindow,
            windowStartUtc,
            windowEndUtc,
            filtered.Count,
            significantEvents.Count,
            outlierEvents.Count);

        return new ShiftHandoffReportDto
        {
            Date = date,
            ShiftWindow = shiftWindow,
            WindowStartUtc = windowStartUtc,
            WindowEndUtc = windowEndUtc,
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

    private static IReadOnlyList<ObservationEventDto> MapEvents(IEnumerable<ObservationEvent> events) =>
        events.Select(e => new ObservationEventDto
        {
            Id = (Guid)e.Id,
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
