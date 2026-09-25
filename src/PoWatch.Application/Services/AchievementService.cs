using System.Globalization;
using PoWatch.Application.Contracts;
using PoWatch.Domain.Models;
using PoWatch.Domain.Services;
using PoWatch.Shared.Models;

namespace PoWatch.Application.Services;

/// <summary>
/// Stat family E: gathers what the achievement and record rules look at, unlocks what was earned
/// and keeps personal bests. Runs after every newly counted batch and at session start.
/// </summary>
public sealed class AchievementService(
    IRollupStore rollups,
    ISessionRepository sessions,
    IAchievementStore store,
    TimeProvider time)
{
    private const int SessionHistory = 1_000;

    /// <summary>Unlocks newly earned achievements, updates records, and returns just the new unlocks.</summary>
    /// <param name="sessionId">The session that just started or sent data; its time zone defines "today".</param>
    public async Task<IReadOnlyList<AchievementDto>> EvaluateAsync(string userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var history = await sessions.ListAsync(userId, SessionHistory, cancellationToken);
        var zone = history.FirstOrDefault(s => s.Id == sessionId)?.TimeZone ?? TimeZoneInfo.Utc;
        var today = LocalDay.Of(now, zone);
        var (todayStart, _) = LocalDay.Window(today, zone);

        var allTime = await rollups.GetAllTimeAsync(userId, cancellationToken);
        var todayHourly = await rollups.GetRangeAsync(userId, RollupGrain.Hour, todayStart, now.AddHours(1), cancellationToken);

        var context = new AchievementContext
        {
            NowUtc = now,
            Zone = zone,
            SessionCount = history.Count,
            LongestSession = history.Count == 0 ? TimeSpan.Zero : history.Max(s => s.Duration(now)),
            DailyStreak = DailyStreak(history, today, now, zone),
            AllTime = allTime,
            Today = todayHourly.Aggregate(Rollup.Empty, Rollup.Merge),
            TodayHourly = todayHourly
        };

        var unlocked = await store.GetUnlockedAsync(userId, cancellationToken);
        var unlocks = AchievementRules.Evaluate(context, unlocked.Keys.ToHashSet(StringComparer.Ordinal));
        if (unlocks.Count > 0)
            await store.UnlockAsync(userId, unlocks, cancellationToken);

        var records = await store.GetRecordsAsync(userId, cancellationToken);
        var beaten = RecordRules.Update(records,
        [
            new(RecordRules.LongestSession, context.LongestSession.TotalSeconds, now),
            new(RecordRules.PeakConcurrency, context.Today.PeakConcurrency, now),
        ]);
        // A zero is not a record; it would only fill the cabinet with "0 at once".
        beaten = beaten.Where(r => r.Value > 0).ToList();
        if (beaten.Count > 0)
            await store.SaveRecordsAsync(userId, beaten, cancellationToken);

        var byId = AchievementRules.All.ToDictionary(a => a.Id, StringComparer.Ordinal);
        return unlocks.Select(u => ToDto(byId[u.AchievementId], u.UnlockedAtUtc)).ToList();
    }

    public async Task<TrophyCabinetDto> CabinetAsync(string userId, CancellationToken cancellationToken)
    {
        var unlocked = await store.GetUnlockedAsync(userId, cancellationToken);
        var records = await store.GetRecordsAsync(userId, cancellationToken);

        return new TrophyCabinetDto
        {
            Achievements = AchievementRules.All
                .Select(a => ToDto(a, unlocked.TryGetValue(a.Id, out var at) ? at : null))
                .OrderByDescending(a => a.Unlocked)
                .ThenByDescending(a => a.UnlockedAtUtc)
                .ToList(),
            Records =
            [
                .. from r in RecordRules.All
                   where records.ContainsKey(r.Id)
                   let e = records[r.Id]
                   select new RecordDto { Id = r.Id, Title = r.Title, Value = e.Value, Display = Display(r.Id, e.Value), SetAtUtc = e.SetAtUtc, PreviousValue = e.PreviousValue }
            ]
        };
    }

    /// <summary>Consecutive local days, ending today, on which some session was running.</summary>
    internal static int DailyStreak(IReadOnlyList<Session> history, DateOnly today, DateTimeOffset now, TimeZoneInfo zone)
    {
        var days = new HashSet<DateOnly>();
        foreach (var session in history)
        {
            var last = LocalDay.Of(session.EndedUtc ?? now, zone);
            for (var day = LocalDay.Of(session.StartedUtc, zone); day <= last; day = day.AddDays(1))
                days.Add(day);
        }

        var streak = 0;
        for (var day = today; days.Contains(day); day = day.AddDays(-1))
            streak++;
        return streak;
    }

    private static AchievementDto ToDto(AchievementDefinition a, DateTimeOffset? unlockedAtUtc) => new()
    {
        Id = a.Id,
        Title = a.Title,
        Description = a.Description,
        UnlockedAtUtc = unlockedAtUtc
    };

    private static string Display(string recordId, double value) => recordId switch
    {
        RecordRules.LongestSession => TemplateRecap.Duration(TimeSpan.FromSeconds(value)),
        RecordRules.PeakConcurrency => string.Create(CultureInfo.InvariantCulture, $"{value:0} at once"),
        _ => value.ToString("0.##", CultureInfo.InvariantCulture)
    };
}
