using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PoWatch.Application.Contracts;
using PoWatch.Shared.Models;
using PoWatch.Application.Options;
using PoWatch.Application.Services;
using PoWatch.Domain.Models;
using PoWatch.Infrastructure.Runtime;

namespace PoWatch.UnitTests;

public sealed class ObservationServiceTests
{
    [Fact]
    public async Task IngestAsync_DropsPoll_WhenGateIsBusy()
    {
        var service = BuildService(new BusyGate(), out _, out _);

        var result = await service.IngestAsync(new IngestObservationRequestDto
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

        var result = await service.IngestAsync(new IngestObservationRequestDto
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

        var result = await service.IngestAsync(new IngestObservationRequestDto
        {
            SubjectHint = "Kim",
            Activity = "Desk Work",
            ClinicalPayload = "<S>Kim is working at the desk.<E>",
            IsSignificant = true,
            SignificantReason = "Known person entered"
        }, CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.Single(observations.Items);
        Assert.False(string.IsNullOrWhiteSpace(result.ImageReference));
        Assert.Equal(observations.Items[0].ImageReference, result.ImageReference);
        Assert.EndsWith(".svg", observations.Items[0].ImageReference, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IngestAsync_DoesNotPersistDuplicateActivity_ForSameSubject()
    {
        var service = BuildService(new OpenGate(), out var observations, out _);

        await service.IngestAsync(new IngestObservationRequestDto
        {
            SubjectHint = "Kim",
            Activity = "Desk Work",
            ClinicalPayload = "<S>Kim is working at the desk.<E>"
        }, CancellationToken.None);

        var second = await service.IngestAsync(new IngestObservationRequestDto
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
    public async Task IngestAsync_RejectsRepetitiveLowQualityOutput_WhenSanitizerEnabled()
    {
        var service = BuildService(new OpenGate(), out var observations, out _, new FeatureFlagsOptions
        {
            EnableTelemetrySanitizer = true
        });

        var result = await service.IngestAsync(new IngestObservationRequestDto
        {
            Activity = "The art of the art of the art of the art of the art",
            ClinicalPayload = "The art of the art of the art of the art of the art of the art of the art of the art."
        }, CancellationToken.None);

        Assert.False(result.Accepted);
        Assert.True(result.Dropped);
        Assert.Empty(observations.Items);
        Assert.Contains("telemetry sanitizer", result.Detail, StringComparison.OrdinalIgnoreCase);
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
            new TelemetryContentSanitizer(),
            new AlertThresholdEvaluator(
                Microsoft.Extensions.Options.Options.Create(new PoWatch.Application.Options.AlertThresholdOptions()),
                NullLogger<AlertThresholdEvaluator>.Instance),
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

        public Task<IReadOnlyList<ObservationEvent>> GetByDateRangeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ObservationEvent>>(Items);

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
                // Reuse the existing profile if the subject ID already matches (preserves LastActivity across calls).
                if (!string.Equals(_subject.SubjectId, hint, StringComparison.OrdinalIgnoreCase))
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

        public Task UpdateLastActivityAsync(string subjectId, string activity, bool isOutlier, CancellationToken cancellationToken)
        {
            if (string.Equals(_subject.SubjectId, subjectId, StringComparison.OrdinalIgnoreCase))
            {
                _subject.LastActivity = activity;
                _subject.LastActivityIsOutlier = isOutlier;
            }

            return Task.CompletedTask;
        }
    }
}

