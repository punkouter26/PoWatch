using PoWatch.Application.Contracts;
using PoWatch.Application.Services;
using PoWatch.Domain.Models;

namespace PoWatch.Unit;

public sealed class ArchivesServiceTests
{
    [Fact]
    public async Task GetChapterAsync_ReturnsEmptyChapterMessage_WhenNoDataExists()
    {
        var service = new ArchivesService(new FakeObservationRepository([]), Microsoft.Extensions.Logging.Abstractions.NullLogger<ArchivesService>.Instance);

        var chapter = await service.GetChapterAsync(new DateOnly(2026, 4, 14), CancellationToken.None);

        Assert.Empty(chapter.Timeline);
        Assert.Empty(chapter.Highlights);
        Assert.Equal("No data for this day.", chapter.ClinicalNarrative);
    }

    [Fact]
    public async Task GetChapterAsync_ReturnsOnlySignificantHighlights_InReverseChronologicalOrder()
    {
        var items = new[]
        {
            new ObservationEvent
            {
                SubjectId = SubjectId.From("Kim"),
                SubjectDisplayName = "Kim",
                Activity = "Desk Work",
                ClinicalDescription = "Focused work",
                IsSignificant = false,
                ObservedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-20)
            },
            new ObservationEvent
            {
                SubjectId = SubjectId.From("Maya"),
                SubjectDisplayName = "Maya",
                Activity = "Entered",
                ClinicalDescription = "Entered room",
                IsSignificant = true,
                SignificantReason = "Arrival",
                ImageReference = "significant-images/20260414/maya/a.jpg",
                ObservedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
            },
            new ObservationEvent
            {
                SubjectId = SubjectId.From("Kim"),
                SubjectDisplayName = "Kim",
                Activity = "Break",
                ClinicalDescription = "Step away",
                IsSignificant = true,
                SignificantReason = "State change",
                ImageReference = "significant-images/20260414/kim/b.jpg",
                ObservedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
            }
        };

        var service = new ArchivesService(new FakeObservationRepository(items), Microsoft.Extensions.Logging.Abstractions.NullLogger<ArchivesService>.Instance);

        var chapter = await service.GetChapterAsync(DateOnly.FromDateTime(DateTime.UtcNow), CancellationToken.None);

        Assert.Equal(3, chapter.Timeline.Count);
        Assert.Equal(2, chapter.Highlights.Count);
        Assert.All(chapter.Highlights, x => Assert.True(x.IsSignificant));
        Assert.Equal("Kim", chapter.Highlights[0].SubjectDisplayName);
    }

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
