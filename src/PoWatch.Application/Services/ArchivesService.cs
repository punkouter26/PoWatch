using Microsoft.Extensions.Logging;
using PoWatch.Application.Contracts;
using PoWatch.Domain.Models;

namespace PoWatch.Application.Services;

public sealed class ArchivesService(IObservationRepository observationRepository, ILogger<ArchivesService> logger)
{
    public async Task<DailyChapter> GetChapterAsync(DateOnly date, CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading daily chapter. Date={Date}", date);

        var timeline = (await observationRepository.GetByDateAsync(date, cancellationToken))
            .OrderBy(x => x.ObservedAtUtc)
            .ToList();

        var highlights = timeline
            .Where(x => x.IsSignificant)
            .OrderByDescending(x => x.ObservedAtUtc)
            .Take(30)
            .ToList();

        var occupiedCount = timeline.Count;
        var outlierCount = timeline.Count(x => x.IsClinicalOutlier);
        var primaryActivity = timeline
            .GroupBy(x => x.Activity)
            .OrderByDescending(x => x.Count())
            .Select(x => x.Key)
            .FirstOrDefault() ?? "No detected activity";

        var primarySubject = timeline
            .GroupBy(x => x.SubjectDisplayName)
            .OrderByDescending(x => x.Count())
            .Select(x => x.Key)
            .FirstOrDefault() ?? "No identified subject";

        var narrative = occupiedCount == 0
            ? "No data for this day."
            : $"The room recorded {occupiedCount} events. Primary subject: {primarySubject}. Dominant activity: {primaryActivity}. Clinical outliers flagged: {outlierCount}.";

        logger.LogInformation(
            "Daily chapter loaded. Date={Date} TimelineCount={TimelineCount} HighlightCount={HighlightCount}",
            date,
            timeline.Count,
            highlights.Count);

        return new DailyChapter
        {
            Date = date,
            Timeline = timeline,
            Highlights = highlights,
            ClinicalNarrative = narrative
        };
    }
}
