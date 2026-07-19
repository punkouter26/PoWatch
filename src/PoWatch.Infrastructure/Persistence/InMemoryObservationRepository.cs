using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PoWatch.Application.Contracts;
using PoWatch.Domain.Models;

namespace PoWatch.Infrastructure.Persistence;

public sealed class InMemoryObservationRepository(ILogger<InMemoryObservationRepository> logger) : IObservationRepository
{
    private static readonly ActivitySource ActivitySource = new("PoWatch.Storage");
    private readonly ConcurrentDictionary<Guid, ObservationEvent> _events = new();

    public Task AddAsync(ObservationEvent observation, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("Storage.AddObservation");
        activity?.SetTag("powatch.subject_id", observation.SubjectId.Value);

        _events[observation.Id] = observation;

        // TraceId is already on the Activity/Serilog context — don't eagerly ToString() it every write.
        logger.LogDebug("In-memory observation storage write completed. SubjectId={SubjectId}", observation.SubjectId);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ObservationEvent>> GetByDateAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var items = _events.Values
            .Where(x => DateOnly.FromDateTime(x.ObservedAtUtc.UtcDateTime) == date)
            .OrderBy(x => x.ObservedAtUtc)
            .ToList();

        logger.LogDebug(
            "In-memory observation chapter read completed. Date={Date} Count={Count} TraceId={TraceId}",
            date,
            items.Count,
            Activity.Current?.TraceId.ToString());

        return Task.FromResult<IReadOnlyList<ObservationEvent>>(items);
    }

    public Task<IReadOnlyList<ObservationEvent>> GetByDateRangeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var items = _events.Values
            .Where(x =>
            {
                var d = DateOnly.FromDateTime(x.ObservedAtUtc.UtcDateTime);
                return d >= from && d <= to;
            })
            .OrderBy(x => x.ObservedAtUtc)
            .ToList();

        return Task.FromResult<IReadOnlyList<ObservationEvent>>(items);
    }

    public Task<IReadOnlyList<ObservationEvent>> GetBySubjectAndDateRangeAsync(
        string subjectId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var items = _events.Values
            .Where(x =>
                string.Equals(x.SubjectId, subjectId, StringComparison.OrdinalIgnoreCase) &&
                DateOnly.FromDateTime(x.ObservedAtUtc.UtcDateTime) >= from &&
                DateOnly.FromDateTime(x.ObservedAtUtc.UtcDateTime) <= to)
            .OrderBy(x => x.ObservedAtUtc)
            .ToList();

        logger.LogDebug(
            "In-memory filtered observation read completed. SubjectId={SubjectId} From={From} To={To} Count={Count}",
            subjectId,
            from,
            to,
            items.Count);

        return Task.FromResult<IReadOnlyList<ObservationEvent>>(items);
    }

    public Task<ObservationEvent?> GetLatestForSubjectAsync(string subjectId, CancellationToken cancellationToken)
    {
        var item = _events.Values
            .Where(x => string.Equals(x.SubjectId, subjectId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.ObservedAtUtc)
            .FirstOrDefault();

        return Task.FromResult(item);
    }

    public Task<int> MergeSubjectAsync(string oldSubjectId, SubjectProfile target, CancellationToken cancellationToken)
    {
        var rewritten = _events.Values
            .Where(x => string.Equals(x.SubjectId, oldSubjectId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var old in rewritten)
        {
            var replacement = new ObservationEvent
            {
                Id = old.Id,
                ObservedAtUtc = old.ObservedAtUtc,
                SubjectId = target.SubjectId,
                SubjectDisplayName = target.DisplayName,
                Activity = old.Activity,
                ClinicalDescription = old.ClinicalDescription,
                IsSignificant = old.IsSignificant,
                SignificantReason = old.SignificantReason,
                IsClinicalOutlier = old.IsClinicalOutlier,
                ImageReference = old.ImageReference
            };

            _events[replacement.Id] = replacement;
        }

        logger.LogInformation(
            "In-memory subject history rewrite completed. OldSubjectId={OldSubjectId} CanonicalSubjectId={CanonicalSubjectId} EventsRewritten={EventsRewritten} TraceId={TraceId}",
            oldSubjectId,
            target.SubjectId,
            rewritten.Count,
            Activity.Current?.TraceId.ToString());

        return Task.FromResult(rewritten.Count);
    }
}
