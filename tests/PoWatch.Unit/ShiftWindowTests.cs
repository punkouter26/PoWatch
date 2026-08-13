using Microsoft.Extensions.Logging.Abstractions;
using PoWatch.Application.Contracts;
using PoWatch.Application.Services;
using PoWatch.Domain.Models;
using PoWatch.Shared.Models;

namespace PoWatch.Unit;

/// <summary>
/// Shift boundaries are local wall-clock hours; storage is partitioned by UTC date. These tests pin
/// the translation between the two, which is the seam where the Night shift used to lose half its
/// events and every shift drifted by the UTC offset.
/// </summary>
public sealed class ShiftWindowTests
{
    private static readonly DateOnly Day = new(2026, 4, 14);

    private static DateTimeOffset LocalAt(int hour, int minute = 0, int dayOffset = 0)
    {
        var local = Day.ToDateTime(TimeOnly.MinValue).AddDays(dayOffset).AddHours(hour).AddMinutes(minute);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    // ── ShiftClock.WindowFor ──────────────────────────────────────────────────

    [Theory]
    [InlineData(ShiftWindow.Morning, 6, 14)]
    [InlineData(ShiftWindow.Afternoon, 14, 22)]
    public void WindowFor_MapsDayShiftsToTheirLocalHours(ShiftWindow window, int startHour, int endHour)
    {
        var (startUtc, endUtc) = ShiftClock.WindowFor(Day, window);

        Assert.Equal(LocalAt(startHour), startUtc);
        Assert.Equal(LocalAt(endHour), endUtc);
    }

    [Fact]
    public void WindowFor_NightRunsIntoTheFollowingMorning()
    {
        var (startUtc, endUtc) = ShiftClock.WindowFor(Day, ShiftWindow.Night);

        // A night shift is one continuous stretch of work. The previous implementation treated it as
        // 22:00–24:00 plus 00:00–06:00 of the SAME calendar day — two disjoint pieces eight hours
        // apart, so a brief for "the night of the 14th" described two different nights.
        Assert.Equal(LocalAt(22), startUtc);
        Assert.Equal(LocalAt(6, 0, 1), endUtc);
    }

    [Fact]
    public void WindowFor_FullDayCoversLocalMidnightToMidnight()
    {
        var (startUtc, endUtc) = ShiftClock.WindowFor(Day, ShiftWindow.FullDay);

        Assert.Equal(LocalAt(0), startUtc);
        Assert.Equal(LocalAt(0, 0, 1), endUtc);
    }

    [Fact]
    public void WindowFor_ShiftsTileTheDayWithoutGapOrOverlap()
    {
        var morning = ShiftClock.WindowFor(Day, ShiftWindow.Morning);
        var afternoon = ShiftClock.WindowFor(Day, ShiftWindow.Afternoon);
        var night = ShiftClock.WindowFor(Day, ShiftWindow.Night);
        var nextMorning = ShiftClock.WindowFor(Day.AddDays(1), ShiftWindow.Morning);

        Assert.Equal(morning.EndUtc, afternoon.StartUtc);
        Assert.Equal(afternoon.EndUtc, night.StartUtc);
        Assert.Equal(night.EndUtc, nextMorning.StartUtc);
    }

    // ── ReportService end-to-end over those windows ───────────────────────────

    [Fact]
    public async Task AfternoonReport_UsesHalfOpenBoundaries()
    {
        var report = await BuildReport(ShiftWindow.Afternoon,
            Event("Kim", "Just before", LocalAt(13, 59)),
            Event("Kim", "On the boundary", LocalAt(14, 0)),
            Event("Kim", "Inside", LocalAt(20, 0)),
            Event("Kim", "On the far boundary", LocalAt(22, 0)));

        Assert.Equal(2, report.TotalEvents);
        Assert.Equal(LocalAt(14), report.WindowStartUtc);
        Assert.Equal(LocalAt(22), report.WindowEndUtc);
    }

    [Fact]
    public async Task NightReport_IncludesTheSmallHoursOfTheFollowingDay()
    {
        ObservationEvent[] events =
        [
            Event("Kim", "Same-day small hours", LocalAt(2, 0)),   // belongs to the PREVIOUS night
            Event("Kim", "Shift start", LocalAt(22, 30)),
            Event("Kim", "Overnight round", LocalAt(3, 0, 1)),
            Event("Kim", "After handover", LocalAt(6, 30, 1))      // past the 06:00 handover
        ];

        var report = await BuildReport(ShiftWindow.Night, events);
        var activities = await BuildReportActivities(ShiftWindow.Night, events);

        Assert.Equal(2, report.TotalEvents);
        Assert.Equal(["Shift start", "Overnight round"], activities);
    }

    [Fact]
    public async Task FullDayReport_ExcludesAdjacentDays()
    {
        var report = await BuildReport(ShiftWindow.FullDay,
            Event("Kim", "Yesterday", LocalAt(23, 0, -1)),
            Event("Kim", "Today", LocalAt(0, 0)),
            Event("Kim", "Also today", LocalAt(23, 59)),
            Event("Kim", "Tomorrow", LocalAt(0, 0, 1)));

        Assert.Equal(2, report.TotalEvents);
    }

    [Fact]
    public async Task Report_HumanizesThePrimarySubject()
    {
        var report = await BuildReport(ShiftWindow.Afternoon, Event("Subject-529", "Standing", LocalAt(15)));

        Assert.Equal("Person 529", report.PrimarySubject);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Task<ShiftHandoffReportDto> BuildReport(ShiftWindow window, params ObservationEvent[] events) =>
        new ReportService(new WindowFakeRepository(events), NullLogger<ReportService>.Instance)
            .BuildHandoffReportAsync(Day, window, CancellationToken.None);

    private static async Task<IReadOnlyList<string>> BuildReportActivities(ShiftWindow window, params ObservationEvent[] events)
    {
        // The report DTO exposes counts and flagged events but not the raw timeline, so the covered
        // set is re-derived through the same window the report used.
        var (startUtc, endUtc) = ShiftClock.WindowFor(Day, window);
        var covered = await ShiftClock.LoadWindowAsync(new WindowFakeRepository(events), startUtc, endUtc, CancellationToken.None);
        return covered.Select(e => e.Activity).ToList();
    }

    private static ObservationEvent Event(string subject, string activity, DateTimeOffset at) => new()
    {
        SubjectId = SubjectId.From(subject),
        SubjectDisplayName = subject,
        Activity = activity,
        ClinicalDescription = activity,
        ObservedAtUtc = at
    };

    /// <summary>
    /// Returns every event for any partition query. That is deliberate: it forces the instant-level
    /// trim in ShiftClock to be what selects the window, rather than the fake quietly pre-filtering.
    /// </summary>
    private sealed class WindowFakeRepository(IReadOnlyList<ObservationEvent> events) : IObservationRepository
    {
        public Task AddAsync(ObservationEvent observation, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<ObservationEvent>> GetByDateAsync(DateOnly date, CancellationToken cancellationToken) =>
            Task.FromResult(events);

        public Task<IReadOnlyList<ObservationEvent>> GetByDateRangeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
            Task.FromResult(events);

        public Task<ObservationEvent?> GetLatestForSubjectAsync(string subjectId, CancellationToken cancellationToken) =>
            Task.FromResult(events.Count > 0 ? events[^1] : null);

        public Task<int> MergeSubjectAsync(string oldSubjectId, SubjectProfile target, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<IReadOnlyList<ObservationEvent>> GetBySubjectAndDateRangeAsync(
            string subjectId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ObservationEvent>>(
                events.Where(e => string.Equals(e.SubjectId, subjectId, StringComparison.OrdinalIgnoreCase)).ToList());
    }
}
