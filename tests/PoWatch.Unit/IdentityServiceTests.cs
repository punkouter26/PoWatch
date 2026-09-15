using Microsoft.Extensions.Logging.Abstractions;
using PoWatch.Application.Contracts;
using PoWatch.Shared.Models;
using PoWatch.Application.Services;
using PoWatch.Domain.Models;
using PoWatch.Infrastructure.Runtime;

namespace PoWatch.Unit;

public sealed class IdentityServiceTests
{
    [Fact]
    public async Task RenameAsync_CanonicalizesSubjectAndRewritesHistory()
    {
        var observations = new FakeObservationRepository();
        var subjects = new FakeSubjectRepository(
            new SubjectProfile
            {
                SubjectId = SubjectId.From("Subject-1"),
                DisplayName = "Subject-1",
                IdentityStatus = IdentityStatus.Temporary,
                FirstSeenUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                LastSeenUtc = DateTimeOffset.UtcNow
            });

        observations.Items.Add(new ObservationEvent
        {
            SubjectId = SubjectId.From("Subject-1"),
            SubjectDisplayName = "Subject-1",
            Activity = "Desk Work",
            ClinicalDescription = "Observed at desk."
        });

        var service = new IdentityService(subjects, observations, new InMemoryAcknowledgementRegistry(), new PoWatch.Infrastructure.Persistence.InMemorySubjectRevisionEventRepository(), NullLogger<IdentityService>.Instance);

        var result = await service.RenameAsync("Subject-1", new RenameSubjectRequestDto { NewName = "Maya" }, "tester", CancellationToken.None);

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
                SubjectId = SubjectId.From("kim"),
                DisplayName = "Kim",
                IdentityStatus = IdentityStatus.Known,
                FirstSeenUtc = DateTimeOffset.UtcNow.AddHours(-1),
                LastSeenUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
            },
            new SubjectProfile
            {
                SubjectId = SubjectId.From("Subject-2"),
                DisplayName = "Subject-2",
                IdentityStatus = IdentityStatus.Temporary,
                FirstSeenUtc = DateTimeOffset.UtcNow.AddHours(-2),
                LastSeenUtc = DateTimeOffset.UtcNow
            });

        observations.Items.Add(new ObservationEvent
        {
            SubjectId = SubjectId.From("Subject-2"),
            SubjectDisplayName = "Subject-2",
            Activity = "Walking",
            ClinicalDescription = "Observed walking."
        });

        var service = new IdentityService(subjects, observations, new InMemoryAcknowledgementRegistry(), new PoWatch.Infrastructure.Persistence.InMemorySubjectRevisionEventRepository(), NullLogger<IdentityService>.Instance);

        var result = await service.MergeAsync(new MergeIdentityRequestDto
        {
            PrimarySubjectId = SubjectId.From("kim"),
            SecondarySubjectId = SubjectId.From("Subject-2"),
            NewDisplayName = "Kim"
        }, "tester", CancellationToken.None);

        Assert.Equal("kim", result.CanonicalSubjectId);
        Assert.Equal("Kim", result.CanonicalName);
        Assert.Equal(1, result.EventsRewritten);
        Assert.Equal(1, result.SubjectsRemoved);
        Assert.DoesNotContain(subjects.Items.Keys, key => key == "Subject-2");
    }

    // ── Revision history ──────────────────────────────────────────────────────

    [Fact]
    public async Task RenameAsync_AppendsRenamedEventAgainstTheCanonicalId()
    {
        var observations = new FakeObservationRepository();
        var subjects = new FakeSubjectRepository(
            new SubjectProfile { SubjectId = SubjectId.From("Subject-1"), DisplayName = "Subject-1", IdentityStatus = IdentityStatus.Temporary });
        var revisions = new PoWatch.Infrastructure.Persistence.InMemorySubjectRevisionEventRepository();

        var service = new IdentityService(subjects, observations, new InMemoryAcknowledgementRegistry(), revisions, NullLogger<IdentityService>.Instance);

        await service.RenameAsync("Subject-1", new RenameSubjectRequestDto { NewName = "Maya" }, "tester", CancellationToken.None);

        var history = await revisions.GetHistoryAsync("maya", CancellationToken.None);
        Assert.Single(history);
        Assert.Equal(Domain.Models.SubjectRevisionKind.Renamed, history[0].Kind);
        Assert.Equal("tester", history[0].ActorUserId);
        Assert.Contains("Maya", history[0].Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenameAsync_RecordsDeletedEntryForTheAbsorbedId_WhenIdChanges()
    {
        var observations = new FakeObservationRepository();
        var subjects = new FakeSubjectRepository(
            new SubjectProfile { SubjectId = SubjectId.From("Subject-1"), DisplayName = "Subject-1", IdentityStatus = IdentityStatus.Temporary });
        var revisions = new PoWatch.Infrastructure.Persistence.InMemorySubjectRevisionEventRepository();

        var service = new IdentityService(subjects, observations, new InMemoryAcknowledgementRegistry(), revisions, NullLogger<IdentityService>.Instance);

        await service.RenameAsync("Subject-1", new RenameSubjectRequestDto { NewName = "Maya" }, "tester", CancellationToken.None);

        // The absorbed id keeps its history: a Renamed row under the canonical id AND a Deleted
        // row under the original id, so a future GET /history on either id is meaningful.
        var canonical = await revisions.GetHistoryAsync("maya", CancellationToken.None);
        var absorbed = await revisions.GetHistoryAsync("Subject-1", CancellationToken.None);
        Assert.Single(canonical);
        Assert.Equal(Domain.Models.SubjectRevisionKind.Renamed, canonical[0].Kind);
        Assert.Single(absorbed);
        Assert.Equal(Domain.Models.SubjectRevisionKind.Deleted, absorbed[0].Kind);
    }

    [Fact]
    public async Task MergeAsync_RecordsMergedFromForCanonical_AndMergedIntoAndDeletedForAbsorbed()
    {
        var observations = new FakeObservationRepository();
        var subjects = new FakeSubjectRepository(
            new SubjectProfile { SubjectId = SubjectId.From("kim"), DisplayName = "Kim", IdentityStatus = IdentityStatus.Known },
            new SubjectProfile { SubjectId = SubjectId.From("Subject-2"), DisplayName = "Subject-2", IdentityStatus = IdentityStatus.Temporary });
        var revisions = new PoWatch.Infrastructure.Persistence.InMemorySubjectRevisionEventRepository();

        var service = new IdentityService(subjects, observations, new InMemoryAcknowledgementRegistry(), revisions, NullLogger<IdentityService>.Instance);

        await service.MergeAsync(new MergeIdentityRequestDto
        {
            PrimarySubjectId = SubjectId.From("kim"),
            SecondarySubjectId = SubjectId.From("Subject-2"),
            NewDisplayName = "Kim"
        }, "tester", CancellationToken.None);

        var canonical = await revisions.GetHistoryAsync("kim", CancellationToken.None);
        var absorbed = await revisions.GetHistoryAsync("Subject-2", CancellationToken.None);

        Assert.Single(canonical);
        Assert.Equal(Domain.Models.SubjectRevisionKind.MergedFrom, canonical[0].Kind);

        Assert.Equal(2, absorbed.Count);
        Assert.Contains(absorbed, e => e.Kind == Domain.Models.SubjectRevisionKind.MergedInto);
        Assert.Contains(absorbed, e => e.Kind == Domain.Models.SubjectRevisionKind.Deleted);
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsEventsMostRecentFirst()
    {
        var observations = new FakeObservationRepository();
        var subjects = new FakeSubjectRepository(
            new SubjectProfile { SubjectId = SubjectId.From("kim"), DisplayName = "Kim", IdentityStatus = IdentityStatus.Known });
        var revisions = new PoWatch.Infrastructure.Persistence.InMemorySubjectRevisionEventRepository();

        var service = new IdentityService(subjects, observations, new InMemoryAcknowledgementRegistry(), revisions, NullLogger<IdentityService>.Instance);

        // Force two events with a noticeable timestamp gap (the repo orders by OccurredAtUtc).
        await revisions.AppendAsync(new SubjectRevisionEvent { SubjectId = SubjectId.From("kim"), OccurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-2), Kind = Domain.Models.SubjectRevisionKind.Created }, CancellationToken.None);
        await revisions.AppendAsync(new SubjectRevisionEvent { SubjectId = SubjectId.From("kim"), OccurredAtUtc = DateTimeOffset.UtcNow, Kind = Domain.Models.SubjectRevisionKind.Renamed, Detail = "later" }, CancellationToken.None);

        var history = await service.GetHistoryAsync("kim", CancellationToken.None);

        Assert.Equal(2, history.Count);
        Assert.Equal(Shared.Models.SubjectRevisionKind.Renamed, history[0].Kind);
        Assert.Equal(Shared.Models.SubjectRevisionKind.Created, history[1].Kind);
        Assert.Equal("later", history[0].Detail);
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
                    SubjectId = SubjectId.From(target.SubjectId),
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
            primary.IdentityStatus = IdentityStatus.Known;
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
                SubjectId = SubjectId.From(newDisplayName.ToLowerInvariant()),
                DisplayName = newDisplayName,
                IdentityStatus = IdentityStatus.Known,
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
            Task.FromResult(new SubjectProfile { SubjectId = SubjectId.From(displayName.ToLowerInvariant().Replace(" ", "-")), DisplayName = displayName, IdentityStatus = IdentityStatus.Known });
    }
}
