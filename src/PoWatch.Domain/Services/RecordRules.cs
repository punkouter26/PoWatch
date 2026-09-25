namespace PoWatch.Domain.Services;

/// <summary>A value that might set a record, e.g. today's visit count for "busiest-day-visits".</summary>
public sealed record RecordCandidate(string RecordId, double Value, DateTimeOffset AtUtc);

public sealed record RecordEntry(string RecordId, double Value, DateTimeOffset SetAtUtc, double? PreviousValue);

/// <summary>Stat family E — personal bests.</summary>
public static class RecordRules
{
    public const string LongestSession = "longest-session";
    public const string BusiestDayVisits = "busiest-day-visits";
    public const string PeakConcurrency = "peak-concurrency";
    public const string DailyStreak = "daily-streak";

    /// <summary>Every record, in cabinet order, with its display title.</summary>
    public static readonly IReadOnlyList<(string Id, string Title)> All =
    [
        (LongestSession, "Longest session"),
        (BusiestDayVisits, "Busiest day"),
        (PeakConcurrency, "Most at once"),
        (DailyStreak, "Longest daily streak"),
    ];

    /// <summary>
    /// The records that <paramref name="candidates"/> beat. Only a strictly higher value counts, so
    /// replaying the same candidates after applying the result changes nothing.
    /// </summary>
    public static IReadOnlyList<RecordEntry> Update(IReadOnlyDictionary<string, RecordEntry> current, IEnumerable<RecordCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(candidates);

        return candidates
            .GroupBy(c => c.RecordId, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(c => c.Value).ThenBy(c => c.AtUtc).First())
            .Where(c => !current.TryGetValue(c.RecordId, out var existing) || c.Value > existing.Value)
            .Select(c => new RecordEntry(c.RecordId, c.Value, c.AtUtc, current.TryGetValue(c.RecordId, out var existing) ? existing.Value : null))
            .ToList();
    }
}
