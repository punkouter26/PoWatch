using PoWatch.Application.Contracts;
using PoWatch.Infrastructure.Runtime;
using PoWatch.Shared.Models;

namespace PoWatch.Unit;

public sealed class TemplateHandoffSummarizerTests
{
    private static readonly DateOnly Day = new(2026, 4, 14);

    [Fact]
    public async Task PriorityItems_HumanizeStorageSubjectIds()
    {
        var content = await Summarize(BuildContext(
            outliers: [Event("Subject-529", "Fell", 15, isOutlier: true)],
            drift: [Drift("Subject-546", DriftLabels.Extreme, 100)]));

        // The brief is read next to a timeline that says "Person 529". Naming the same person by a
        // storage id here made the reader translate between two vocabularies mid-handoff.
        Assert.All(content.PriorityItems, item =>
            Assert.DoesNotContain("Subject-", item, StringComparison.Ordinal));
        Assert.Contains(content.PriorityItems, i => i.Contains("Person 529", StringComparison.Ordinal));
        Assert.Contains(content.PriorityItems, i => i.Contains("Person 546", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DriftItems_AreMarkedAsOutsideTheShiftWindow()
    {
        var content = await Summarize(BuildContext(drift: [Drift("Subject-546", DriftLabels.High, 80)]));

        // Drift scores today against a multi-day baseline; it is not a thing that happened during
        // these hours, and an unqualified "DRIFT ALERT" sent readers hunting for it in the timeline.
        var driftItem = Assert.Single(content.PriorityItems, i => i.StartsWith("DRIFT ALERT", StringComparison.Ordinal));
        Assert.Contains("not this window", driftItem, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(content.SourceNotes, n => n.Contains("not scoped to this shift", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PriorityItems_DiscloseTruncationRatherThanSilentlyCapping()
    {
        var outliers = Enumerable.Range(0, 9)
            .Select(i => Event("Subject-1", $"Event {i}", 14, isOutlier: true))
            .ToList();

        var content = await Summarize(BuildContext(outliers: outliers));

        // Showing 5 of 9 with no note reads as "there were 5".
        Assert.Contains(content.PriorityItems, i => i.Contains("4 further outlier", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Summary_StatesTheHoursCovered()
    {
        var content = await Summarize(BuildContext(totalEvents: 58));

        // "Afternoon shift: 58 observations" was indistinguishable from a full-day count.
        Assert.Contains("14:00–22:00", content.Summary, StringComparison.Ordinal);
        Assert.Contains(content.SourceNotes, n => n.Contains("Window covered", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Summary_SaysSoExplicitly_WhenNothingWasFlagged()
    {
        var content = await Summarize(BuildContext(totalEvents: 58));

        Assert.Contains("Nothing was flagged for review", content.Summary, StringComparison.Ordinal);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Task<HandoffSummaryContent> Summarize(HandoffSummarizerContext context) =>
        new TemplateHandoffSummarizer().SummarizeAsync(context, CancellationToken.None);

    private static HandoffSummarizerContext BuildContext(
        IReadOnlyList<ObservationEventDto>? outliers = null,
        IReadOnlyList<ObservationEventDto>? significant = null,
        IReadOnlyList<SubjectDriftStatusDto>? drift = null,
        int totalEvents = 1)
    {
        outliers ??= [];
        significant ??= [];

        return new HandoffSummarizerContext
        {
            ShiftWindow = nameof(ShiftWindow.Afternoon),
            Audience = "NurseToNurse",
            DriftStatus = drift ?? [],
            Report = new ShiftHandoffReportDto
            {
                Date = Day,
                ShiftWindow = ShiftWindow.Afternoon,
                WindowStartUtc = LocalAt(14),
                WindowEndUtc = LocalAt(22),
                PrimarySubject = "Person 529",
                DominantActivity = "Standing",
                TotalEvents = Math.Max(totalEvents, outliers.Count + significant.Count),
                OutlierCount = outliers.Count,
                SignificantCount = significant.Count,
                OutlierEvents = outliers,
                SignificantEvents = significant
            }
        };
    }

    private static DateTimeOffset LocalAt(int hour)
    {
        var local = Day.ToDateTime(TimeOnly.MinValue).AddHours(hour);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    private static ObservationEventDto Event(string subject, string activity, int hour, bool isOutlier = false) => new()
    {
        Id = Guid.NewGuid(),
        ObservedAtUtc = LocalAt(hour),
        SubjectId = subject,
        SubjectDisplayName = subject,
        Activity = activity,
        ClinicalDescription = activity,
        IsSignificant = true,
        IsClinicalOutlier = isOutlier
    };

    private static SubjectDriftStatusDto Drift(string name, string label, double score) => new()
    {
        SubjectId = name,
        DisplayName = name,
        DriftLabel = label,
        DriftScore = score
    };
}
