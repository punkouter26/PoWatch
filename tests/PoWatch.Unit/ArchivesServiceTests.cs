using PoWatch.Application.Contracts;
using PoWatch.Application.Services;
using PoWatch.Domain.Models;

namespace PoWatch.Unit;

public sealed class ArchivesServiceTests
{
    private static readonly DateOnly Day = new(2026, 4, 14);

    /// <summary>An instant at the given local wall-clock time on <see cref="Day"/>.</summary>
    private static DateTimeOffset LocalAt(int hour, int minute = 0, int dayOffset = 0)
    {
        var local = Day.ToDateTime(TimeOnly.MinValue).AddDays(dayOffset).AddHours(hour).AddMinutes(minute);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    [Fact]
    public async Task GetChapterAsync_ReturnsEmptyChapterMessage_WhenNoDataExists()
    {
        var service = new ArchivesService(new FakeObservationRepository([]), Microsoft.Extensions.Logging.Abstractions.NullLogger<ArchivesService>.Instance);

        var chapter = await service.GetChapterAsync(Day, CancellationToken.None);

        Assert.Empty(chapter.Timeline);
        Assert.Empty(chapter.Highlights);
        Assert.Equal("No observations were recorded on this day.", chapter.ClinicalNarrative);
        Assert.Equal(0, chapter.TotalEvents);
    }

    [Fact]
    public async Task GetChapterAsync_ReturnsOnlySignificantHighlights_InReverseChronologicalOrder()
    {
        var items = new[]
        {
            Event("Kim", "Desk Work", LocalAt(9)),
            Event("Maya", "Entered", LocalAt(10), isSignificant: true, reason: "Arrival"),
            Event("Kim", "Break", LocalAt(11), isSignificant: true, reason: "State change")
        };

        var service = new ArchivesService(new FakeObservationRepository(items), Microsoft.Extensions.Logging.Abstractions.NullLogger<ArchivesService>.Instance);

        var chapter = await service.GetChapterAsync(Day, CancellationToken.None);

        Assert.Equal(3, chapter.Timeline.Count);
        Assert.Equal(2, chapter.Highlights.Count);
        Assert.All(chapter.Highlights, x => Assert.True(x.IsSignificant));
        Assert.Equal("Kim", chapter.Highlights[0].SubjectDisplayName);
    }

    // ── Local-day boundary ────────────────────────────────────────────────────
    // Storage partitions by UTC date but the caller's date is a local calendar day. Unless the two
    // happen to coincide, a chapter built straight from one partition drops one end of the evening
    // and borrows the small hours of the next day.

    [Fact]
    public async Task GetChapterAsync_IncludesLateEvening_AndExcludesTheFollowingDay()
    {
        var items = new[]
        {
            Event("Kim", "Settling", LocalAt(23, 30)),          // last half hour of the local day
            Event("Kim", "Next morning", LocalAt(7, 0, 1)),     // belongs to the following day
            Event("Kim", "Previous night", LocalAt(23, 0, -1))  // belongs to the previous day
        };

        var service = new ArchivesService(new FakeObservationRepository(items), Microsoft.Extensions.Logging.Abstractions.NullLogger<ArchivesService>.Instance);

        var chapter = await service.GetChapterAsync(Day, CancellationToken.None);

        Assert.Single(chapter.Timeline);
        Assert.Equal("Settling", chapter.Timeline[0].Activity);
    }

    [Fact]
    public async Task GetChapterAsync_IncludesTheFirstMinuteOfTheLocalDay()
    {
        var items = new[] { Event("Kim", "Midnight check", LocalAt(0, 0)) };

        var service = new ArchivesService(new FakeObservationRepository(items), Microsoft.Extensions.Logging.Abstractions.NullLogger<ArchivesService>.Instance);

        var chapter = await service.GetChapterAsync(Day, CancellationToken.None);

        Assert.Single(chapter.Timeline);
    }

    // ── Narrative ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetChapterAsync_DoesNotClaimNothingUnusual_WhenNotableEventsExist()
    {
        var items = new[]
        {
            Event("Subject-529", "Standing", LocalAt(14)),
            Event("Subject-529", "Left the room", LocalAt(15), isSignificant: true, reason: "State change")
        };

        var service = new ArchivesService(new FakeObservationRepository(items), Microsoft.Extensions.Logging.Abstractions.NullLogger<ArchivesService>.Instance);

        var chapter = await service.GetChapterAsync(Day, CancellationToken.None);

        // The old wording keyed only off the outlier count, so a day with flagged events on screen
        // was still summarized as "Nothing unusual was flagged."
        Assert.DoesNotContain("Nothing unusual was flagged", chapter.ClinicalNarrative, StringComparison.Ordinal);
        Assert.Contains("1 notable moment was flagged", chapter.ClinicalNarrative, StringComparison.Ordinal);
        Assert.Equal(1, chapter.NotableCount);
        Assert.Equal(0, chapter.OutlierCount);
    }

    [Fact]
    public async Task GetChapterAsync_CountsOutliersAndNotableSeparately_WithoutDoubleCounting()
    {
        var items = new[]
        {
            Event("Kim", "Fell", LocalAt(14), isSignificant: true, reason: "Fall", isOutlier: true),
            Event("Kim", "Left", LocalAt(15), isSignificant: true, reason: "State change")
        };

        var service = new ArchivesService(new FakeObservationRepository(items), Microsoft.Extensions.Logging.Abstractions.NullLogger<ArchivesService>.Instance);

        var chapter = await service.GetChapterAsync(Day, CancellationToken.None);

        // An outlier is always also significant; counting both would report three flags for two events.
        Assert.Equal(1, chapter.OutlierCount);
        Assert.Equal(1, chapter.NotableCount);
        Assert.Equal(2, chapter.TotalEvents);
    }

    [Fact]
    public async Task GetChapterAsync_HumanizesTheSubjectNameInTheNarrative()
    {
        var items = new[] { Event("Subject-529", "Standing", LocalAt(14)) };

        var service = new ArchivesService(new FakeObservationRepository(items), Microsoft.Extensions.Logging.Abstractions.NullLogger<ArchivesService>.Instance);

        var chapter = await service.GetChapterAsync(Day, CancellationToken.None);

        Assert.Contains("Person 529", chapter.ClinicalNarrative, StringComparison.Ordinal);
        Assert.DoesNotContain("Subject-529", chapter.ClinicalNarrative, StringComparison.Ordinal);
    }

    private static ObservationEvent Event(
        string subject,
        string activity,
        DateTimeOffset at,
        bool isSignificant = false,
        string? reason = null,
        bool isOutlier = false) => new()
    {
        SubjectId = SubjectId.From(subject),
        SubjectDisplayName = subject,
        Activity = activity,
        ClinicalDescription = activity,
        IsSignificant = isSignificant || isOutlier,
        SignificantReason = reason,
        IsClinicalOutlier = isOutlier,
        ObservedAtUtc = at
    };

    private sealed class FakeObservationRepository(IEnumerable<ObservationEvent> items) : IObservationRepository
    {
        private readonly List<ObservationEvent> _items = items.ToList();

        public Task AddAsync(ObservationEvent observation, CancellationToken cancellationToken)
        {
            _items.Add(observation);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ObservationEvent>> GetByDateAsync(DateOnly date, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ObservationEvent>>(_items);

        public Task<ObservationEvent?> GetLatestForSubjectAsync(string subjectId, CancellationToken cancellationToken) =>
            Task.FromResult(_items.Where(x => x.SubjectId == subjectId).OrderByDescending(x => x.ObservedAtUtc).FirstOrDefault());

        public Task<IReadOnlyList<ObservationEvent>> GetByDateRangeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ObservationEvent>>(_items);

        public Task<int> MergeSubjectAsync(string oldSubjectId, SubjectProfile target, CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<IReadOnlyList<ObservationEvent>> GetBySubjectAndDateRangeAsync(
            string subjectId,
            DateOnly from,
            DateOnly to,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ObservationEvent>>(
                _items.Where(e => string.Equals(e.SubjectId, subjectId, StringComparison.OrdinalIgnoreCase)).ToList());
    }
}
