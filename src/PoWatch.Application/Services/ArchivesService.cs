using Microsoft.Extensions.Logging;
using PoWatch.Application.Contracts;
using PoWatch.Domain.Models;
using PoWatch.Shared.Models;

namespace PoWatch.Application.Services;

public sealed class ArchivesService(IObservationRepository observationRepository, ILogger<ArchivesService> logger)
{
    public async Task<DailyChapter> GetChapterAsync(DateOnly date, CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading daily chapter. Date={Date}", date);

        // The caller's date is the caregiver's calendar day, not a UTC partition key — see ShiftClock.
        var timeline = await ShiftClock.LoadLocalDayAsync(observationRepository, date, cancellationToken);

        var highlights = timeline
            .Where(x => x.IsSignificant)
            .OrderByDescending(x => x.ObservedAtUtc)
            .Take(30)
            .ToList();

        var outlierCount = timeline.Count(x => x.IsClinicalOutlier);
        // An outlier is also flagged significant, so counting both would double-count the same event.
        var notableCount = timeline.Count(x => x.IsSignificant && !x.IsClinicalOutlier);
        var subjectCount = timeline
            .Select(x => x.SubjectId.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var narrative = BuildNarrative(timeline, outlierCount, notableCount);

        logger.LogInformation(
            "Daily chapter loaded. Date={Date} TimelineCount={TimelineCount} HighlightCount={HighlightCount} Outliers={Outliers} Notable={Notable}",
            date,
            timeline.Count,
            highlights.Count,
            outlierCount,
            notableCount);

        return new DailyChapter
        {
            Date = date,
            Timeline = timeline,
            Highlights = highlights,
            ClinicalNarrative = narrative,
            TotalEvents = timeline.Count,
            OutlierCount = outlierCount,
            NotableCount = notableCount,
            SubjectCount = subjectCount,
            FirstEventUtc = timeline.Count > 0 ? timeline[0].ObservedAtUtc : null,
            LastEventUtc = timeline.Count > 0 ? timeline[^1].ObservedAtUtc : null
        };
    }

    private static string BuildNarrative(IReadOnlyList<ObservationEvent> timeline, int outlierCount, int notableCount)
    {
        if (timeline.Count == 0)
            return "No observations were recorded on this day.";

        var primarySubjectGroup = timeline
            .GroupBy(x => x.SubjectDisplayName)
            .OrderByDescending(x => x.Count())
            .First();

        // Humanized with the SAME helper the client uses, so the narrative no longer says
        // "Primary subject: Subject-116" on a page where every other element says "Person 116".
        var primarySubject = SubjectDisplayNames.Humanize(primarySubjectGroup.Key);
        var primaryActivity = timeline
            .GroupBy(x => x.Activity)
            .OrderByDescending(x => x.Count())
            .Select(x => x.Key)
            .First();

        var from = timeline[0].ObservedAtUtc.ToLocalTime();
        var to = timeline[^1].ObservedAtUtc.ToLocalTime();

        // "58 events recorded" alone gave a caregiver no way to tell a busy morning from a camera
        // that ran all night, so the span and the share attributable to the main subject are stated.
        var opening = timeline.Count == 1
            ? $"1 observation recorded at {from:HH:mm}."
            : $"{timeline.Count} observations recorded between {from:HH:mm} and {to:HH:mm}.";

        var focus = $" Most active: {primarySubject} ({primarySubjectGroup.Count()} of {timeline.Count}). "
                    + $"Most common activity: {primaryActivity}.";

        // The old wording claimed "Nothing unusual was flagged" whenever the outlier count was zero,
        // even with notable events sitting in the same timeline — the two counts are now reported
        // separately so the sentence can never contradict what the rows underneath show.
        var flags = (outlierCount, notableCount) switch
        {
            (0, 0) => " Nothing was flagged as unusual or notable.",
            (0, _) => $" {Plural(notableCount, "notable moment")} flagged; nothing was marked unusual.",
            (_, 0) => $" {Plural(outlierCount, "unusual event")} flagged for review.",
            _ => $" {Plural(outlierCount, "unusual event")} and {Plural(notableCount, "other notable moment")} flagged for review."
        };

        return opening + focus + flags;
    }

    private static string Plural(int count, string noun) =>
        count == 1 ? $"1 {noun} was" : $"{count} {noun}s were";
}
