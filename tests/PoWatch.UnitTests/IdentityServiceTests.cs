using Microsoft.Extensions.Logging.Abstractions;
using PoWatch.Application.Contracts;
using PoWatch.Shared.Models;
using PoWatch.Application.Services;
using PoWatch.Domain.Models;
using PoWatch.Infrastructure.Runtime;

namespace PoWatch.UnitTests;

public sealed class IdentityServiceTests
{
    [Fact]
    public async Task RenameAsync_CanonicalizesSubjectAndRewritesHistory()
    {
        var observations = new FakeObservationRepository();
        var subjects = new FakeSubjectRepository(
            new SubjectProfile
            {
                SubjectId = "Subject-1",
                DisplayName = "Subject-1",
                IsKnownIdentity = false,
                FirstSeenUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                LastSeenUtc = DateTimeOffset.UtcNow
            });

        observations.Items.Add(new ObservationEvent
        {
            SubjectId = "Subject-1",
            SubjectDisplayName = "Subject-1",
            Activity = "Desk Work",
            ClinicalDescription = "Observed at desk."
        });

        var service = new IdentityService(subjects, observations, new InMemoryAcknowledgementRegistry(), NullLogger<IdentityService>.Instance);

        var result = await service.RenameAsync("Subject-1", new RenameSubjectRequestDto { NewName = "Maya" }, CancellationToken.None);

        Assert.Equal("maya", result.CanonicalSubjectId);
        Assert.Equal("Maya", result.CanonicalName);
        Assert.Equal(1, result.EventsRewritten);
        Assert.Contains(subjects.Items.Keys, key => key == "maya");
        Assert.DoesNotContain(subjects.Items.Keys, key => key == "Subject-1");
        Assert.All(observations.Items, item =>
        {
            Assert.Equal("maya", item.SubjectId);
            Assert.Equal("Maya", item.SubjectDisplayName);
        });
    }

    [Fact]
    public async Task MergeAsync_RemovesSecondarySubjectAndReportsRewriteCount()
    {
        var observations = new FakeObservationRepository();
        var subjects = new FakeSubjectRepository(
            new SubjectProfile
            {
                SubjectId = "kim",
                DisplayName = "Kim",
                IsKnownIdentity = true,
                FirstSeenUtc = DateTimeOffset.UtcNow.AddHours(-1),
                LastSeenUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
            },
            new SubjectProfile
            {
                SubjectId = "Subject-2",
                DisplayName = "Subject-2",
                IsKnownIdentity = false,
                FirstSeenUtc = DateTimeOffset.UtcNow.AddHours(-2),
                LastSeenUtc = DateTimeOffset.UtcNow
            });

        observations.Items.Add(new ObservationEvent
        {
            SubjectId = "Subject-2",
            SubjectDisplayName = "Subject-2",
            Activity = "Walking",
            ClinicalDescription = "Observed walking."
        });

        var service = new IdentityService(subjects, observations, new InMemoryAcknowledgementRegistry(), NullLogger<IdentityService>.Instance);

        var result = await service.MergeAsync(new MergeIdentityRequestDto
        {
            PrimarySubjectId = "kim",
            SecondarySubjectId = "Subject-2",
            NewDisplayName = "Kim"
        }, CancellationToken.None);

        Assert.Equal("kim", result.CanonicalSubjectId);
        Assert.Equal("Kim", result.CanonicalName);
        Assert.Equal(1, result.EventsRewritten);
        Assert.Equal(1, result.SubjectsRemoved);
        Assert.DoesNotContain(subjects.Items.Keys, key => key == "Subject-2");
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
            Task.FromResult(Items.LastOrDefault(x => x.SubjectId == subjectId));

        public Task<int> MergeSubjectAsync(string oldSubjectId, SubjectProfile target, CancellationToken cancellationToken)
        {
            var rewritten = 0;
            for (var index = 0; index < Items.Count; index++)
            {
                if (!string.Equals(Items[index].SubjectId, oldSubjectId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                rewritten++;
                Items[index] = new ObservationEvent
                {
                    Id = Items[index].Id,
                    ObservedAtUtc = Items[index].ObservedAtUtc,
                    SubjectId = target.SubjectId,
                    SubjectDisplayName = target.DisplayName,
                    Activity = Items[index].Activity,
                    ClinicalDescription = Items[index].ClinicalDescription,
                    IsSignificant = Items[index].IsSignificant,
                    SignificantReason = Items[index].SignificantReason,
                    IsClinicalOutlier = Items[index].IsClinicalOutlier,
                    ImageReference = Items[index].ImageReference
                };
            }

            return Task.FromResult(rewritten);
        }

        public Task<IReadOnlyList<ObservationEvent>> GetByDateRangeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ObservationEvent>>(Items);

        public Task<IReadOnlyList<ObservationEvent>> GetBySubjectAndDateRangeAsync(
            string subjectId,
            DateOnly from,
            DateOnly to,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ObservationEvent>>(
                Items.Where(e => string.Equals(e.SubjectId, subjectId, StringComparison.OrdinalIgnoreCase)).ToList());
    }

    private sealed class FakeSubjectRepository : ISubjectRepository
    {
        public Dictionary<string, SubjectProfile> Items { get; } = new(StringComparer.OrdinalIgnoreCase);

        public FakeSubjectRepository(params SubjectProfile[] subjects)
        {
            foreach (var subject in subjects)
            {
                Items[subject.SubjectId] = subject;
            }
        }

        public Task<IReadOnlyList<SubjectProfile>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SubjectProfile>>(Items.Values.ToList());

        public Task<SubjectProfile> GetOrCreateAsync(string? hint, CancellationToken cancellationToken) =>
            Task.FromResult(Items.Values.First());

        public Task<SubjectProfile?> GetByIdAsync(string subjectId, CancellationToken cancellationToken)
        {
            Items.TryGetValue(subjectId, out var subject);
            return Task.FromResult(subject);
        }

        public Task<SubjectProfile> MergeAsync(string primarySubjectId, string secondarySubjectId, string? explicitName, CancellationToken cancellationToken)
        {
            var primary = Items[primarySubjectId];
            primary.DisplayName = explicitName ?? primary.DisplayName;
            primary.IsKnownIdentity = true;
            Items.Remove(secondarySubjectId);
            Items[primary.SubjectId] = primary;
            return Task.FromResult(primary);
        }

        public Task<SubjectProfile> RenameAsync(string subjectId, string newDisplayName, CancellationToken cancellationToken)
        {
            var subject = Items[subjectId];
            Items.Remove(subjectId);

            var renamed = new SubjectProfile
            {
                SubjectId = newDisplayName.ToLowerInvariant(),
                DisplayName = newDisplayName,
                IsKnownIdentity = true,
                FirstSeenUtc = subject.FirstSeenUtc,
                LastSeenUtc = subject.LastSeenUtc
            };

            Items[renamed.SubjectId] = renamed;
            return Task.FromResult(renamed);
        }

        public Task UpdateLastActivityAsync(string subjectId, string activity, bool isOutlier, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task DeleteAsync(string subjectId, CancellationToken cancellationToken)
        {
            Items.Remove(subjectId);
            return Task.CompletedTask;
        }

        public Task<SubjectProfile> RegisterKnownAsync(string displayName, CancellationToken cancellationToken) =>
            Task.FromResult(new SubjectProfile { SubjectId = displayName.ToLowerInvariant().Replace(" ", "-"), DisplayName = displayName, IsKnownIdentity = true });
    }
}
