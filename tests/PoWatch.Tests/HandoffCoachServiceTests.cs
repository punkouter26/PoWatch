using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PoWatch.Application.Contracts;
using PoWatch.Application.Options;
using PoWatch.Application.Services;
using PoWatch.Domain.Models;
using PoWatch.Shared.Models;

namespace PoWatch.Tests;

public sealed class HandoffCoachServiceTests
{
    private static readonly HandoffCoachOptions DefaultHandoffOptions = new()
    {
        MaxPromptSignificantEvents = 20,
        MaxPromptOutlierEvents = 10,
        AllowFamilySafeSummary = false
    };

    private static readonly FeatureFlagsOptions FlagsWithDriftEnabled = new()
    {
        DriftRadarEnabled = true,
        HandoffCoachEnabled = true
    };

    private static readonly FeatureFlagsOptions FlagsWithDriftDisabled = new()
    {
        DriftRadarEnabled = false,
        HandoffCoachEnabled = true
    };

    // ── Audience handling ─────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateBriefAsync_UsesNurseToNurse_WhenAudienceIsInvalid()
    {
        var captured = new CapturingSummarizer();
        var service = BuildService(captured);

        await service.GenerateBriefAsync(
            DateOnly.FromDateTime(DateTime.UtcNow),
            new GenerateHandoffBriefRequestDto { ShiftWindow = "FullDay", Audience = "InvalidAudience" },
            CancellationToken.None);

        Assert.Equal("NurseToNurse", captured.LastContext!.Audience);
    }

    [Fact]
    public async Task GenerateBriefAsync_FallsBackToNurseToNurse_WhenFamilySafeDisabled()
    {
        var captured = new CapturingSummarizer();
        var service = BuildService(captured, handoffOptions: new HandoffCoachOptions { MaxPromptSignificantEvents = 20, MaxPromptOutlierEvents = 10, AllowFamilySafeSummary = false });

        await service.GenerateBriefAsync(
            DateOnly.FromDateTime(DateTime.UtcNow),
            new GenerateHandoffBriefRequestDto { ShiftWindow = "FullDay", Audience = "FamilySafe" },
            CancellationToken.None);

        // FamilySafe blocked → NurseToNurse
        Assert.Equal("NurseToNurse", captured.LastContext!.Audience);
    }

    [Fact]
    public async Task GenerateBriefAsync_AllowsFamilySafe_WhenOptionEnabled()
    {
        var captured = new CapturingSummarizer();
        var service = BuildService(captured, handoffOptions: new HandoffCoachOptions { MaxPromptSignificantEvents = 20, MaxPromptOutlierEvents = 10, AllowFamilySafeSummary = true });

        await service.GenerateBriefAsync(
            DateOnly.FromDateTime(DateTime.UtcNow),
            new GenerateHandoffBriefRequestDto { ShiftWindow = "FullDay", Audience = "FamilySafe" },
            CancellationToken.None);

        Assert.Equal("FamilySafe", captured.LastContext!.Audience);
    }

    [Theory]
    [InlineData("NurseToNurse")]
    [InlineData("Supervisor")]
    public async Task GenerateBriefAsync_PassesThroughValidAudiences(string audience)
    {
        var captured = new CapturingSummarizer();
        var service = BuildService(captured);

        await service.GenerateBriefAsync(
            DateOnly.FromDateTime(DateTime.UtcNow),
            new GenerateHandoffBriefRequestDto { ShiftWindow = "FullDay", Audience = audience },
            CancellationToken.None);

        Assert.Equal(audience, captured.LastContext!.Audience);
    }

    // ── Shift window parsing ──────────────────────────────────────────────────

    [Fact]
    public async Task GenerateBriefAsync_DefaultsToFullDay_WhenShiftWindowIsInvalid()
    {
        var captured = new CapturingSummarizer();
        var service = BuildService(captured);

        await service.GenerateBriefAsync(
            DateOnly.FromDateTime(DateTime.UtcNow),
            new GenerateHandoffBriefRequestDto { ShiftWindow = "UnknownShift", Audience = "NurseToNurse" },
            CancellationToken.None);

        Assert.Equal("FullDay", captured.LastContext!.ShiftWindow);
    }

    // ── DTO mapping ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateBriefAsync_MapsHandoffSummaryContentToDto()
    {
        var stubContent = new HandoffSummaryContent
        {
            Summary = "Shift was calm.",
            PriorityItems = ["Watch subject A"],
            FollowUps = ["Follow up on vitals"],
            SourceNotes = ["PoWatch template"],
            IsAiGenerated = false
        };

        var service = BuildService(new StubSummarizer(stubContent));

        var dto = await service.GenerateBriefAsync(
            DateOnly.FromDateTime(DateTime.UtcNow),
            new GenerateHandoffBriefRequestDto { ShiftWindow = "FullDay", Audience = "NurseToNurse" },
            CancellationToken.None);

        Assert.Equal("Shift was calm.", dto.Summary);
        Assert.Single(dto.PriorityItems);
        Assert.Equal("Watch subject A", dto.PriorityItems[0]);
        Assert.Single(dto.FollowUps);
        Assert.Single(dto.SourceNotes);
        Assert.False(dto.IsAiGenerated);
    }

    [Fact]
    public async Task GenerateBriefAsync_PropagatesToIsAiGenerated_WhenAiPath()
    {
        var stubContent = new HandoffSummaryContent
        {
            Summary = "AI summary.",
            PriorityItems = [],
            FollowUps = [],
            SourceNotes = [],
            IsAiGenerated = true
        };

        var service = BuildService(new StubSummarizer(stubContent));

        var dto = await service.GenerateBriefAsync(
            DateOnly.FromDateTime(DateTime.UtcNow),
            new GenerateHandoffBriefRequestDto { ShiftWindow = "FullDay", Audience = "Supervisor" },
            CancellationToken.None);

        Assert.True(dto.IsAiGenerated);
    }

    // ── Drift context ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateBriefAsync_IncludesDriftContext_WhenDriftRadarEnabled()
    {
        var captured = new CapturingSummarizer();
        var service = BuildService(captured, featureFlags: FlagsWithDriftEnabled,
            subjects: [Subject("alice", "Alice")],
            todayDriftEvents: DriftEvents("alice", 5, 10));

        await service.GenerateBriefAsync(
            DateOnly.FromDateTime(DateTime.UtcNow),
            new GenerateHandoffBriefRequestDto { ShiftWindow = "FullDay", Audience = "NurseToNurse" },
            CancellationToken.None);

        // Drift status should be populated when drift radar is enabled
        Assert.NotNull(captured.LastContext!.DriftStatus);
    }

    [Fact]
    public async Task GenerateBriefAsync_ExcludesDriftContext_WhenDriftRadarDisabled()
    {
        var captured = new CapturingSummarizer();
        var service = BuildService(captured, featureFlags: FlagsWithDriftDisabled,
            subjects: [Subject("bob", "Bob")],
            todayDriftEvents: DriftEvents("bob", 5, 10));

        await service.GenerateBriefAsync(
            DateOnly.FromDateTime(DateTime.UtcNow),
            new GenerateHandoffBriefRequestDto { ShiftWindow = "FullDay", Audience = "NurseToNurse" },
            CancellationToken.None);

        Assert.Empty(captured.LastContext!.DriftStatus);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static HandoffCoachService BuildService(
        IHandoffSummarizer summarizer,
        HandoffCoachOptions? handoffOptions = null,
        FeatureFlagsOptions? featureFlags = null,
        SubjectProfile[]? subjects = null,
        List<ObservationEvent>? todayDriftEvents = null)
    {
        handoffOptions ??= DefaultHandoffOptions;
        featureFlags ??= FlagsWithDriftDisabled;
        subjects ??= [];
        todayDriftEvents ??= [];

        var fakeRepo = new FakeHandoffObservationRepository(todayDriftEvents);
        var reportService = new ReportService(fakeRepo, NullLogger<ReportService>.Instance);
        var driftService = new DriftRadarService(
            new FakeHandoffSubjectRepository(subjects),
            fakeRepo,
            Options.Create(new DriftRadarOptions { MinEventsForDrift = 3 }),
            NullLogger<DriftRadarService>.Instance);

        return new HandoffCoachService(
            reportService,
            driftService,
            summarizer,
            Options.Create(handoffOptions),
            Options.Create(featureFlags),
            NullLogger<HandoffCoachService>.Instance);
    }

    private static SubjectProfile Subject(string id, string name) => new()
    {
        SubjectId = id,
        DisplayName = name,
        IsKnownIdentity = true,
        FirstSeenUtc = DateTimeOffset.UtcNow.AddDays(-10),
        LastSeenUtc = DateTimeOffset.UtcNow
    };

    private static List<ObservationEvent> DriftEvents(string subjectId, int count, int hourOfDay)
    {
        var utcHour = (hourOfDay - (int)TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalHours + 24) % 24;
        return Enumerable.Range(0, count)
            .Select(i => new ObservationEvent
            {
                SubjectId = subjectId,
                SubjectDisplayName = subjectId,
                Activity = "Test",
                ClinicalDescription = "Test",
                ObservedAtUtc = DateTimeOffset.UtcNow.Date.AddHours(utcHour + i)
            })
            .ToList();
    }

    // Stub / spy implementations ──────────────────────────────────────────────

    private sealed class CapturingSummarizer : IHandoffSummarizer
    {
        public HandoffSummarizerContext? LastContext { get; private set; }

        public Task<HandoffSummaryContent> SummarizeAsync(HandoffSummarizerContext context, CancellationToken cancellationToken)
        {
            LastContext = context;
            return Task.FromResult(new HandoffSummaryContent
            {
                Summary = "Captured.",
                PriorityItems = [],
                FollowUps = [],
                SourceNotes = [],
                IsAiGenerated = false
            });
        }
    }

    private sealed class StubSummarizer(HandoffSummaryContent content) : IHandoffSummarizer
    {
        public Task<HandoffSummaryContent> SummarizeAsync(HandoffSummarizerContext context, CancellationToken cancellationToken) =>
            Task.FromResult(content);
    }

    private sealed class FakeHandoffObservationRepository(IReadOnlyList<ObservationEvent> events) : IObservationRepository
    {
        public Task AddAsync(ObservationEvent observation, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<ObservationEvent>> GetByDateAsync(DateOnly date, CancellationToken cancellationToken) =>
            Task.FromResult(events);

        public Task<ObservationEvent?> GetLatestForSubjectAsync(string subjectId, CancellationToken cancellationToken) =>
            Task.FromResult(events.LastOrDefault(x => x.SubjectId == subjectId));

        public Task<IReadOnlyList<ObservationEvent>> GetByDateRangeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ObservationEvent>>(events.ToList());

        public Task<int> MergeSubjectAsync(string oldSubjectId, SubjectProfile target, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<IReadOnlyList<ObservationEvent>> GetBySubjectAndDateRangeAsync(
            string subjectId,
            DateOnly from,
            DateOnly to,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ObservationEvent>>(
                events.Where(e => string.Equals(e.SubjectId, subjectId, StringComparison.OrdinalIgnoreCase)).ToList());
    }

    private sealed class FakeHandoffSubjectRepository(params SubjectProfile[] subjects) : ISubjectRepository
    {
        private readonly IReadOnlyList<SubjectProfile> _subjects = subjects;

        public Task<IReadOnlyList<SubjectProfile>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_subjects);

        public Task<SubjectProfile> GetOrCreateAsync(string? hint, CancellationToken cancellationToken) =>
            Task.FromResult(_subjects.FirstOrDefault() ?? new SubjectProfile());

        public Task<SubjectProfile?> GetByIdAsync(string subjectId, CancellationToken cancellationToken) =>
            Task.FromResult(_subjects.FirstOrDefault(s => s.SubjectId == subjectId));

        public Task<SubjectProfile> MergeAsync(string primarySubjectId, string secondarySubjectId, string? explicitName, CancellationToken cancellationToken) =>
            Task.FromResult(_subjects.First(s => s.SubjectId == primarySubjectId));

        public Task<SubjectProfile> RenameAsync(string subjectId, string newDisplayName, CancellationToken cancellationToken) =>
            Task.FromResult(_subjects.First(s => s.SubjectId == subjectId));

        public Task UpdateLastActivityAsync(string subjectId, string activity, bool isOutlier, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task DeleteAsync(string subjectId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<SubjectProfile> RegisterKnownAsync(string displayName, CancellationToken cancellationToken) =>
            Task.FromResult(new SubjectProfile { SubjectId = displayName.ToLowerInvariant().Replace(" ", "-"), DisplayName = displayName, IsKnownIdentity = true });
    }
}
