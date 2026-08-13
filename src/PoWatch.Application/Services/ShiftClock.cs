using PoWatch.Application.Contracts;
using PoWatch.Domain.Models;
using PoWatch.Shared.Models;

namespace PoWatch.Application.Services;

/// <summary>
/// Translates a facility-local calendar day (or one shift within it) into the UTC instant window
/// that actually contains its events, and loads exactly that window from storage.
/// <para>
/// This exists because the two ends of the app disagreed about what "a day" is. Observations are
/// partitioned in Table Storage by <em>UTC</em> date (see AzureObservationRepository), but the
/// Archives date picker sends the caregiver's <em>local</em> calendar date and every timestamp in
/// the UI is rendered with ToLocalTime(). Reading one UTC partition and labelling it with local
/// times silently shifts the day by the UTC offset: at UTC-5 the last five hours of the evening
/// landed in tomorrow's partition and vanished from tonight's chapter, while the small hours of
/// the next morning leaked in. The Night shift was worse than shifted — it filtered for local
/// hours &lt; 06:00 inside a partition that, at any negative offset, cannot contain them.
/// </para>
/// <para>
/// "Local" means the server's time zone, which is the convention the rest of the app already uses
/// for shift boundaries. On a facility deployment the API runs in the facility's zone.
/// </para>
/// </summary>
public static class ShiftClock
{
    /// <summary>
    /// The half-open UTC instant window [start, end) covered by <paramref name="window"/> on the
    /// local calendar day <paramref name="localDate"/>.
    /// <list type="bullet">
    /// <item>Morning: 06:00–14:00 local, same day</item>
    /// <item>Afternoon: 14:00–22:00 local, same day</item>
    /// <item>Night: 22:00 local → 06:00 local the <em>following</em> morning — a night shift is one
    /// continuous stretch of work, so a brief for "the night of the 13th" must include the small
    /// hours of the 14th rather than those of the 13th.</item>
    /// <item>FullDay: local midnight to local midnight</item>
    /// </list>
    /// </summary>
    public static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) WindowFor(DateOnly localDate, ShiftWindow window)
    {
        var dayStart = localDate.ToDateTime(TimeOnly.MinValue);

        return window switch
        {
            ShiftWindow.Morning => (ToUtc(dayStart.AddHours(6)), ToUtc(dayStart.AddHours(14))),
            ShiftWindow.Afternoon => (ToUtc(dayStart.AddHours(14)), ToUtc(dayStart.AddHours(22))),
            ShiftWindow.Night => (ToUtc(dayStart.AddHours(22)), ToUtc(dayStart.AddHours(30))),
            _ => (ToUtc(dayStart), ToUtc(dayStart.AddDays(1)))
        };
    }

    /// <summary>Loads every observation whose instant falls in [startUtc, endUtc).</summary>
    /// <remarks>
    /// A local window straddles at most two UTC partitions, so this reads the partition range that
    /// covers it and then trims to the exact instants. The trim is what makes the result correct:
    /// the partitions are whole UTC days and always overhang the window at one or both ends.
    /// </remarks>
    public static async Task<IReadOnlyList<ObservationEvent>> LoadWindowAsync(
        IObservationRepository repository,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        CancellationToken cancellationToken)
    {
        var firstPartition = DateOnly.FromDateTime(startUtc.UtcDateTime);
        // endUtc is exclusive, but an event at 23:59:59 of the last partition still belongs to the
        // window, so the partition containing endUtc is read and then trimmed by the filter below.
        var lastPartition = DateOnly.FromDateTime(endUtc.UtcDateTime);

        var candidates = firstPartition == lastPartition
            ? await repository.GetByDateAsync(firstPartition, cancellationToken)
            : await repository.GetByDateRangeAsync(firstPartition, lastPartition, cancellationToken);

        return candidates
            .Where(e => e.ObservedAtUtc >= startUtc && e.ObservedAtUtc < endUtc)
            .OrderBy(e => e.ObservedAtUtc)
            .ToList();
    }

    /// <summary>Loads a whole local calendar day.</summary>
    public static Task<IReadOnlyList<ObservationEvent>> LoadLocalDayAsync(
        IObservationRepository repository,
        DateOnly localDate,
        CancellationToken cancellationToken)
    {
        var (startUtc, endUtc) = WindowFor(localDate, ShiftWindow.FullDay);
        return LoadWindowAsync(repository, startUtc, endUtc, cancellationToken);
    }

    /// <summary>The local calendar day currently in progress.</summary>
    public static DateOnly Today() => DateOnly.FromDateTime(DateTime.Now);

    /// <summary>
    /// Interprets an unspecified-kind local wall-clock time as an instant. During a DST spring-forward
    /// gap the offset either side of the gap is used, which keeps windows contiguous rather than
    /// throwing on an hour that never existed locally.
    /// </summary>
    private static DateTimeOffset ToUtc(DateTime localWallClock) =>
        new DateTimeOffset(localWallClock, TimeZoneInfo.Local.GetUtcOffset(localWallClock)).ToUniversalTime();
}
