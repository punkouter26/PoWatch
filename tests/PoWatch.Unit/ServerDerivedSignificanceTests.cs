using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PoWatch.Application.Contracts;
using PoWatch.Application.Options;
using PoWatch.Application.Services;
using PoWatch.Domain.Models;
using PoWatch.Infrastructure.Runtime;
using PoWatch.Shared.Models;

namespace PoWatch.Unit;

/// <summary>
/// Significance is decided by the server, not asserted by whatever posts to the ingest endpoint.
/// A caller that supplies its own reason still wins — that is a deliberate assertion (the dev-tool
/// injectors, contract tests) rather than a default.
/// </summary>
public sealed class ServerDerivedSignificanceTests
{
    [Fact]
    public async Task An_ordinary_caption_is_not_flagged_even_when_the_client_claims_it_is()
    {
        var service = BuildService(out var observations);

        var result = await service.IngestAsync(new IngestObservationRequestDto
        {
            SubjectHint = "Kim",
            Activity = "Person seated using laptop",
            ClinicalPayload = "<S>Person seated using laptop.<E>",
            // The old worker set this on every well-formed caption. It must no longer be believed.
            IsSignificant = true
        }, CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.False(result.IsSignificant);
        Assert.False(observations.Items[0].IsSignificant);
        Assert.Null(result.SignificantReason);
    }

    [Fact]
    public async Task A_fall_is_flagged_even_when_the_client_says_nothing()
    {
        var service = BuildService(out var observations);

        var result = await service.IngestAsync(new IngestObservationRequestDto
        {
            SubjectHint = "Kim",
            Activity = "Person has fallen beside the bed",
            ClinicalPayload = "<S>Person has fallen beside the bed.<E>",
            IsSignificant = false
        }, CancellationToken.None);

        Assert.True(result.IsSignificant);
        Assert.True(observations.Items[0].IsSignificant);
        Assert.Contains("fall", result.SignificantReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_explicit_caller_reason_is_honoured()
    {
        var service = BuildService(out var observations);

        var result = await service.IngestAsync(new IngestObservationRequestDto
        {
            SubjectHint = "Kim",
            Activity = "Desk Work",
            ClinicalPayload = "<S>Known subject entered and resumed desk work.<E>",
            IsSignificant = true,
            SignificantReason = "Known person entered"
        }, CancellationToken.None);

        Assert.True(result.IsSignificant);
        Assert.Equal("Known person entered", result.SignificantReason);
        Assert.Equal("Known person entered", observations.Items[0].SignificantReason);
    }

    [Fact]
    public async Task An_explicit_caller_reason_can_also_suppress_the_flag()
    {
        var service = BuildService(out _);

        var result = await service.IngestAsync(new IngestObservationRequestDto
        {
            Activity = "Person has fallen",
            ClinicalPayload = "<S>Rehearsal frame, not a real event.<E>",
            IsSignificant = false,
            SignificantReason = "Suppressed by the caller"
        }, CancellationToken.None);

        Assert.False(result.IsSignificant);
    }

    [Fact]
    public async Task Only_flagged_observations_reserve_an_evidence_image()
    {
        var service = BuildService(out _);

        var routine = await service.IngestAsync(new IngestObservationRequestDto
        {
            Activity = "Person seated using laptop",
            ClinicalPayload = "<S>Person seated using laptop.<E>"
        }, CancellationToken.None);

        var flagged = await service.IngestAsync(new IngestObservationRequestDto
        {
            Activity = "Someone is standing up from the chair",
            ClinicalPayload = "<S>Someone is standing up from the chair.<E>"
        }, CancellationToken.None);

        Assert.Null(routine.ImageReference);
        Assert.False(string.IsNullOrWhiteSpace(flagged.ImageReference));
    }

    [Fact]
    public async Task The_verdict_is_echoed_on_the_response_so_the_client_need_not_guess()
    {
        var service = BuildService(out var observations);

        var result = await service.IngestAsync(new IngestObservationRequestDto
        {
            Activity = "A person entering the room",
            ClinicalPayload = "<S>A person entering the room.<E>"
        }, CancellationToken.None);

        Assert.Equal(observations.Items[0].IsSignificant, result.IsSignificant);
        Assert.Equal(observations.Items[0].SignificantReason, result.SignificantReason);
    }

    [Fact]
    public async Task A_malformed_payload_stays_an_outlier_regardless_of_the_caption()
    {
        var service = BuildService(out var observations);

        var result = await service.IngestAsync(new IngestObservationRequestDto
        {
            Activity = "Person seated using laptop",
            ClinicalPayload = "no tags here"
        }, CancellationToken.None);

        Assert.True(result.IsOutlier);
        Assert.True(observations.Items[0].IsClinicalOutlier);
    }

    private static ObservationService BuildService(out FakeObservations observations)
    {
        observations = new FakeObservations();

        return new ObservationService(
            observations,
            new FakeSubjects(),
            new OpenGate(),
            new TelemetryContentSanitizer(),
            new AlertThresholdEvaluator(
                Options.Create(new AlertThresholdOptions()),
                NullLogger<AlertThresholdEvaluator>.Instance),
            Options.Create(new FeatureFlagsOptions()),
            Options.Create(new ObserverOptions()),
            NullLogger<ObservationService>.Instance);
    }

    private sealed class OpenGate : IObservationProcessingGate
    {
        public void Exit() { }
        public bool TryEnter() => true;
        public bool IsProcessing => false;
    }

    internal sealed class FakeObservations : IObservationRepository
    {
        public List<ObservationEvent> Items { get; } = [];

        public Task AddAsync(ObservationEvent observation, CancellationToken cancellationToken)
        {
            Items.Add(observation);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ObservationEvent>> GetByDateAsync(DateOnly date, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ObservationEvent>>(Items);

        public Task<ObservationEvent?> GetLatestForSubjectAsync(string subjectId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.LastOrDefault(x => x.SubjectId == subjectId));

        public Task<IReadOnlyList<ObservationEvent>> GetByDateRangeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ObservationEvent>>(Items);

        public Task<int> MergeSubjectAsync(string oldSubjectId, SubjectProfile target, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<IReadOnlyList<ObservationEvent>> GetBySubjectAndDateRangeAsync(
            string subjectId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ObservationEvent>>(Items.Where(e => e.SubjectId == subjectId).ToList());
    }

    private sealed class FakeSubjects : ISubjectRepository
    {
        private SubjectProfile _subject = new()
        {
            SubjectId = SubjectId.From("Subject-1"),
            DisplayName = "Subject-1",
            IdentityStatus = IdentityStatus.Temporary,
            FirstSeenUtc = DateTimeOffset.UtcNow,
            LastSeenUtc = DateTimeOffset.UtcNow
        };

        public Task<SubjectProfile> GetOrCreateAsync(string? subjectHint, CancellationToken cancellationToken) =>
            Task.FromResult(_subject);

        public Task<SubjectProfile?> GetByIdAsync(string subjectId, CancellationToken cancellationToken) =>
            Task.FromResult<SubjectProfile?>(_subject);

        public Task<IReadOnlyList<SubjectProfile>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SubjectProfile>>([_subject]);

        public Task<SubjectProfile> RenameAsync(string subjectId, string newDisplayName, CancellationToken cancellationToken) =>
            Task.FromResult(_subject);

        public Task<SubjectProfile> RegisterKnownAsync(string displayName, CancellationToken cancellationToken) =>
            Task.FromResult(_subject);

        public Task DeleteAsync(string subjectId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<SubjectProfile> MergeAsync(string primarySubjectId, string secondarySubjectId, string? newDisplayName, CancellationToken cancellationToken) =>
            Task.FromResult(_subject);

        public Task UpdateLastActivityAsync(string subjectId, string activity, bool isOutlier, CancellationToken cancellationToken)
        {
            _subject = new SubjectProfile
            {
                SubjectId = _subject.SubjectId,
                DisplayName = _subject.DisplayName,
                IdentityStatus = _subject.IdentityStatus,
                FirstSeenUtc = _subject.FirstSeenUtc,
                LastSeenUtc = DateTimeOffset.UtcNow,
                LastActivity = activity,
                LastActivityIsOutlier = isOutlier
            };
            return Task.CompletedTask;
        }
    }
}
