using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PoWatch.Application.Contracts;
using PoWatch.Application.Options;
using PoWatch.Application.Services;
using PoWatch.Domain.Models;

namespace PoWatch.UnitTests;

public sealed class DriftRadarServiceTests
{
    private static readonly DriftRadarOptions DefaultOptions = new()
    {
        MinEventsForDrift = 3,
        HighDriftThreshold = 60.0,
        ModerateDriftThreshold = 30.0,
        SlightDriftThreshold = 10.0,
        BaselineDays = 7,
        MaxInsights = 4
    };

    // ── No subjects ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDriftStatusAsync_ReturnsEmpty_WhenNoSubjectsExist()
    {
        var service = BuildService(subjects: [], todayEvents: [], historicalEvents: []);

        var result = await service.GetDriftStatusAsync(CancellationToken.None);

        Assert.Empty(result);
    }

    // ── Insufficient data ────────────────────────────────────────────────────

    [Fact]
    public async Task GetDriftStatusAsync_ReturnsInsufficientData_WhenTodayEventsBelowMinimum()
    {
        var profiles = new[] { Profile("kim", "Kim") };
        // 2 events today = below MinEventsForDrift (3)
        var todayEvents = Events("kim", count: 2, hourOfDay: 10);

        var service = BuildService(profiles, todayEvents, historicalEvents: []);

        var result = await service.GetDriftStatusAsync(CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("Insufficient Data", result[0].DriftLabel);
        Assert.Equal(0, result[0].DriftScore);
        Assert.Empty(result[0].Insights);
    }

    // ── Drift classification ─────────────────────────────────────────────────

    [Fact]
    public async Task GetDriftStatusAsync_ReturnsNormalLabel_WhenVectorsAreIdentical_DirectClassification()
    {
        // Identical baseline and today → cosine = 1 → drift = 0 → "Normal"
        var profiles = new[] { Profile("p", "P") };
        var events = Events("p", count: 5, hourOfDay: 9);
        var historical = Events("p", count: 35, hourOfDay: 9);
        var svc = BuildService(profiles, events, historical);

        var result = await svc.GetDriftStatusAsync(CancellationToken.None);

        Assert.Equal("Normal", result[0].DriftLabel);
    }

    [Fact]
    public async Task GetDriftStatusAsync_ReturnsNormal_WhenTodayMatchesBaseline()
    {
        var profiles = new[] { Profile("alice", "Alice") };
        // Both baseline (7×5=35) and today (5) all in hour 10 → cosine similarity = 1 → drift = 0
        var today = Events("alice", count: 5, hourOfDay: 10);
        var historical = Events("alice", count: 35, hourOfDay: 10);

        var service = BuildService(profiles, today, historical);
        var result = await service.GetDriftStatusAsync(CancellationToken.None);

        Assert.Equal("Normal", result[0].DriftLabel);
        Assert.Equal(0, result[0].DriftScore);
    }

    [Fact]
    public async Task GetDriftStatusAsync_ReturnsHighDrift_WhenTodayIsOrthogonalToBaseline()
    {
        var profiles = new[] { Profile("bob", "Bob") };
        // Baseline all in hour 8, today all in hour 20 → orthogonal vectors → cosine = 0 → drift = 100
        var today = Events("bob", count: 5, hourOfDay: 20);
        var historical = Events("bob", count: 35, hourOfDay: 8);

        var service = BuildService(profiles, today, historical);
        var result = await service.GetDriftStatusAsync(CancellationToken.None);

        Assert.Equal("Extreme Deviation", result[0].DriftLabel);
        Assert.Equal(100, result[0].DriftScore);
    }

    // ── Insights ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDriftStatusAsync_GeneratesPeakShiftInsight_WhenPeakHourMovedByAtLeastThree()
    {
        var profiles = new[] { Profile("carol", "Carol") };
        // Baseline peak at hour 9, today peak at hour 15 → shift = 6 hours
        var today = Events("carol", count: 5, hourOfDay: 15);
        var historical = Events("carol", count: 35, hourOfDay: 9);

        var service = BuildService(profiles, today, historical);
        var result = await service.GetDriftStatusAsync(CancellationToken.None);

        Assert.Contains(result[0].Insights, i => i.Title.Contains("Peak activity window shifted"));
    }

    [Fact]
    public async Task GetDriftStatusAsync_GeneratesOutlierInsight_WhenOutlierRateExceedsFifteenPercent()
    {
        var profiles = new[] { Profile("dan", "Dan") };
        var today = new List<ObservationEvent>
        {
            new() { SubjectId = "dan", SubjectDisplayName = "dan", Activity = "Test", ClinicalDescription = "Test", IsClinicalOutlier = true },
            new() { SubjectId = "dan", SubjectDisplayName = "dan", Activity = "Test", ClinicalDescription = "Test", IsClinicalOutlier = true },
            new() { SubjectId = "dan", SubjectDisplayName = "dan", Activity = "Test", ClinicalDescription = "Test" },
            new() { SubjectId = "dan", SubjectDisplayName = "dan", Activity = "Test", ClinicalDescription = "Test" },
            new() { SubjectId = "dan", SubjectDisplayName = "dan", Activity = "Test", ClinicalDescription = "Test" },
            new() { SubjectId = "dan", SubjectDisplayName = "dan", Activity = "Test", ClinicalDescription = "Test" },
        };

        var historical = Events("dan", count: 42, hourOfDay: 10);
        var service = BuildService(profiles, today, historical);
        var result = await service.GetDriftStatusAsync(CancellationToken.None);

        Assert.Contains(result[0].Insights, i => i.Title.Contains("outlier rate"));
    }

    [Fact]
    public async Task GetDriftStatusAsync_InsightCountRespectMaxInsightsOption()
    {
        // Use options with MaxInsights = 2
        var opts = Options.Create(new DriftRadarOptions
        {
            MinEventsForDrift = 3,
            HighDriftThreshold = 60.0,
            ModerateDriftThreshold = 30.0,
            SlightDriftThreshold = 10.0,
            BaselineDays = 7,
            MaxInsights = 2
        });

        var profiles = new[] { Profile("eve", "Eve") };
        // Create conditions for multiple insights: peak shift + high outlier rate
        var localOffset = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now);
        var utcHour = (20 - (int)localOffset.TotalHours + 24) % 24;
        var today = Enumerable.Range(0, 6).Select(i => new ObservationEvent
        {
            SubjectId = "eve",
            SubjectDisplayName = "eve",
            Activity = "Test",
            ClinicalDescription = "Test",
            IsClinicalOutlier = i < 2,
            ObservedAtUtc = DateTimeOffset.UtcNow.Date.AddHours(utcHour + i)
        }).ToList();
        var historical = Events("eve", count: 42, hourOfDay: 8);

        var service = new DriftRadarService(
            new FakeSubjectRepository(profiles),
            new FakeDriftObservationRepository(today, historical),
            opts,
            NullLogger<DriftRadarService>.Instance);

        var result = await service.GetDriftStatusAsync(CancellationToken.None);

        Assert.True(result[0].Insights.Count <= 2, "Insights must not exceed MaxInsights.");
    }

    // ── Ordering ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDriftStatusAsync_OrdersResultsByDriftScoreDescending()
    {
        var profiles = new[] { Profile("high", "High"), Profile("low", "Low") };

        // "high": today at hour 20, baseline at hour 8 → high drift
        var todayHigh = Events("high", count: 5, hourOfDay: 20);
        var histHigh = Events("high", count: 35, hourOfDay: 8);

        // "low": today at hour 10, baseline at hour 10 → zero drift
        var todayLow = Events("low", count: 5, hourOfDay: 10);
        var histLow = Events("low", count: 35, hourOfDay: 10);

        var allToday = todayHigh.Concat(todayLow).ToList();
        var allHist = histHigh.Concat(histLow).ToList();

        var service = BuildService(profiles, allToday, allHist);
        var result = await service.GetDriftStatusAsync(CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("high", result[0].SubjectId);
        Assert.Equal("low", result[1].SubjectId);
        Assert.True(result[0].DriftScore > result[1].DriftScore);
    }

    // ── Vectors ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDriftStatusAsync_Returns24ElementHourlyVectors()
    {
        var profiles = new[] { Profile("f", "F") };
        var today = Events("f", count: 4, hourOfDay: 12);
        var historical = Events("f", count: 28, hourOfDay: 12);

        var service = BuildService(profiles, today, historical);
        var result = await service.GetDriftStatusAsync(CancellationToken.None);

        Assert.Equal(24, result[0].HourlyBaselineVector.Count);
        Assert.Equal(24, result[0].HourlyTodayVector.Count);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static DriftRadarService BuildService(
        IEnumerable<SubjectProfile> subjects,
        IEnumerable<ObservationEvent> todayEvents,
        IEnumerable<ObservationEvent> historicalEvents)
    {
        return new DriftRadarService(
            new FakeSubjectRepository(subjects.ToArray()),
            new FakeDriftObservationRepository(todayEvents.ToList(), historicalEvents.ToList()),
            Options.Create(DefaultOptions),
            NullLogger<DriftRadarService>.Instance);
    }

    private static SubjectProfile Profile(string id, string name) => new()
    {
        SubjectId = id,
        DisplayName = name,
        IsKnownIdentity = true,
        FirstSeenUtc = DateTimeOffset.UtcNow.AddDays(-10),
        LastSeenUtc = DateTimeOffset.UtcNow
    };

    /// <summary>Generates <paramref name="count"/> events for the given subject all in the given hour.</summary>
    private static List<ObservationEvent> Events(string subjectId, int count, int hourOfDay)
    {
        var now = DateTimeOffset.UtcNow.Date; // midnight UTC today
        var localOffset = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now);
        // Convert local hour to UTC hour
        var utcHour = (hourOfDay - (int)localOffset.TotalHours + 24) % 24;

        return Enumerable.Range(0, count)
            .Select(i => new ObservationEvent
            {
                SubjectId = subjectId,
                SubjectDisplayName = subjectId,
                Activity = "Test",
                ClinicalDescription = "Test event",
                ObservedAtUtc = new DateTimeOffset(now, TimeSpan.Zero).AddHours(utcHour).AddMinutes(i)
            })
            .ToList();
    }

    // Fake repositories ───────────────────────────────────────────────────────

    private sealed class FakeSubjectRepository(params SubjectProfile[] subjects) : ISubjectRepository
    {
        private readonly IReadOnlyList<SubjectProfile> _subjects = subjects;

        public Task<IReadOnlyList<SubjectProfile>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_subjects);

        public Task<SubjectProfile> GetOrCreateAsync(string? hint, CancellationToken cancellationToken) =>
            Task.FromResult(_subjects[0]);

        public Task<SubjectProfile?> GetByIdAsync(string subjectId, CancellationToken cancellationToken) =>
            Task.FromResult(_subjects.FirstOrDefault(s => s.SubjectId == subjectId));

        public Task<SubjectProfile> MergeAsync(string primarySubjectId, string secondarySubjectId, string? explicitName, CancellationToken cancellationToken) =>
            Task.FromResult(_subjects.First(s => s.SubjectId == primarySubjectId));

        public Task<SubjectProfile> RenameAsync(string subjectId, string newDisplayName, CancellationToken cancellationToken) =>
            Task.FromResult(_subjects.First(s => s.SubjectId == subjectId));

        public Task UpdateLastActivityAsync(string subjectId, string activity, bool isOutlier, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<SubjectProfile> RegisterKnownAsync(string displayName, CancellationToken cancellationToken) =>
            Task.FromResult(new SubjectProfile { SubjectId = displayName.ToLowerInvariant().Replace(" ", "-"), DisplayName = displayName, IsKnownIdentity = true });
    }

    private sealed class FakeDriftObservationRepository(
        IReadOnlyList<ObservationEvent> todayEvents,
        IReadOnlyList<ObservationEvent> historicalEvents) : IObservationRepository
    {
        public Task AddAsync(ObservationEvent observation, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<ObservationEvent>> GetByDateAsync(DateOnly date, CancellationToken cancellationToken) =>
            Task.FromResult(todayEvents);

        public Task<ObservationEvent?> GetLatestForSubjectAsync(string subjectId, CancellationToken cancellationToken) =>
            Task.FromResult(todayEvents.LastOrDefault(x => x.SubjectId == subjectId));

        public Task<IReadOnlyList<ObservationEvent>> GetByDateRangeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
            Task.FromResult(historicalEvents);

        public Task<int> MergeSubjectAsync(string oldSubjectId, SubjectProfile target, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<IReadOnlyList<ObservationEvent>> GetBySubjectAndDateRangeAsync(
            string subjectId,
            DateOnly from,
            DateOnly to,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ObservationEvent>>(
                historicalEvents.Where(e => string.Equals(e.SubjectId, subjectId, StringComparison.OrdinalIgnoreCase)).ToList());
    }
}
