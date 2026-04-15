using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PoWatch.Application.Contracts;
using PoWatch.Application.Models;
using PoWatch.Application.Options;
using PoWatch.Application.Services;
using PoWatch.Domain.Models;

namespace PoWatch.UnitTests;

public sealed class ObservationServiceTests
{
    [Fact]
    public async Task IngestAsync_DropsPoll_WhenGateIsBusy()
    {
        var service = BuildService(new BusyGate(), out _, out _);

        var result = await service.IngestAsync(new IngestObservationRequest
        {
            ClinicalPayload = "<S>OK<E>",
            Activity = "Desk Work"
        }, CancellationToken.None);

        Assert.True(result.Dropped);
        Assert.False(result.Accepted);
    }

    [Fact]
    public async Task IngestAsync_RecordsOutlier_WhenPayloadMalformed()
    {
        var service = BuildService(new OpenGate(), out var observations, out _);

        var result = await service.IngestAsync(new IngestObservationRequest
        {
            ClinicalPayload = "broken",
            Activity = "Unknown"
        }, CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.True(result.IsOutlier);
        Assert.Single(observations.Items);
        Assert.True(observations.Items[0].IsClinicalOutlier);
    }

    [Fact]
    public async Task IngestAsync_ReturnsImageReference_ForSignificantObservation()
    {
        var service = BuildService(new OpenGate(), out var observations, out _);

        var result = await service.IngestAsync(new IngestObservationRequest
        {
            SubjectHint = "Kim",
            Activity = "Desk Work",
            ClinicalPayload = "<S>Kim is working at the desk.<E>",
            IsSignificant = true,
            SignificantReason = "Known person entered"
        }, CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.Single(observations.Items);
        Assert.False(string.IsNullOrWhiteSpace(observations.Items[0].ImageReference));
        Assert.EndsWith(".svg", observations.Items[0].ImageReference, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IngestAsync_DoesNotPersistDuplicateActivity_ForSameSubject()
    {
        var service = BuildService(new OpenGate(), out var observations, out _);

        await service.IngestAsync(new IngestObservationRequest
        {
            SubjectHint = "Kim",
            Activity = "Desk Work",
            ClinicalPayload = "<S>Kim is working at the desk.<E>"
        }, CancellationToken.None);

        var second = await service.IngestAsync(new IngestObservationRequest
        {
            SubjectHint = "Kim",
            Activity = "Desk Work",
            ClinicalPayload = "<S>Kim is still working at the desk.<E>"
        }, CancellationToken.None);

        Assert.True(second.Accepted);
        Assert.Equal("No state change detected; redundant observation skipped.", second.Detail);
        Assert.Single(observations.Items);
    }

    [Fact]
    public void GetRuntimeState_UsesConfiguredPollingInterval()
    {
        var service = BuildService(new OpenGate(), out _, out _, new FeatureFlagsOptions(), new ObserverOptions
        {
            PollingIntervalSeconds = 3
        });

        var state = service.GetRuntimeState();

        Assert.Equal(3, state.PollIntervalSeconds);
    }

    private static ObservationService BuildService(
        IObservationProcessingGate gate,
        out FakeObservationRepository observations,
        out FakeSubjectRepository subjects,
        FeatureFlagsOptions? flags = null,
        ObserverOptions? observerOptions = null)
    {
        observations = new FakeObservationRepository();
        subjects = new FakeSubjectRepository();

        return new ObservationService(
            observations,
            subjects,
            gate,
            Options.Create(flags ?? new FeatureFlagsOptions()),
            Options.Create(observerOptions ?? new ObserverOptions()),
            NullLogger<ObservationService>.Instance);
    }

    private sealed class OpenGate : IObservationProcessingGate
    {
        public void Exit() { }
        public bool TryEnter() => true;
    }

    private sealed class BusyGate : IObservationProcessingGate
    {
        public void Exit() { }
        public bool TryEnter() => false;
    }

    private sealed class FakeObservationRepository : IObservationRepository
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
            Task.FromResult(Items
                .Where(x => string.Equals(x.SubjectId, subjectId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.ObservedAtUtc)
                .FirstOrDefault());

        public Task<int> MergeSubjectAsync(string oldSubjectId, SubjectProfile target, CancellationToken cancellationToken) =>
            Task.FromResult(0);
    }

    private sealed class FakeSubjectRepository : ISubjectRepository
    {
        private SubjectProfile _subject = new()
        {
            SubjectId = "Subject-1",
            DisplayName = "Subject-1",
            FirstSeenUtc = DateTimeOffset.UtcNow,
            LastSeenUtc = DateTimeOffset.UtcNow,
            IsKnownIdentity = false
        };

        public Task<IReadOnlyList<SubjectProfile>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SubjectProfile>>([_subject]);

        public Task<SubjectProfile> GetOrCreateAsync(string? hint, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(hint))
            {
                _subject = new SubjectProfile
                {
                    SubjectId = hint,
                    DisplayName = hint,
                    FirstSeenUtc = DateTimeOffset.UtcNow,
                    LastSeenUtc = DateTimeOffset.UtcNow,
                    IsKnownIdentity = true
                };
            }

            return Task.FromResult(_subject);
        }

        public Task<SubjectProfile?> GetByIdAsync(string subjectId, CancellationToken cancellationToken) =>
            Task.FromResult<SubjectProfile?>(_subject);

        public Task<SubjectProfile> MergeAsync(string primarySubjectId, string secondarySubjectId, string? explicitName, CancellationToken cancellationToken) =>
            Task.FromResult(_subject);

        public Task<SubjectProfile> RenameAsync(string subjectId, string newDisplayName, CancellationToken cancellationToken)
        {
            _subject = new SubjectProfile
            {
                SubjectId = newDisplayName.ToLowerInvariant(),
                DisplayName = newDisplayName,
                FirstSeenUtc = _subject.FirstSeenUtc,
                LastSeenUtc = _subject.LastSeenUtc,
                IsKnownIdentity = true
            };

            return Task.FromResult(_subject);
        }
    }
}

