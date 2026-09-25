namespace PoWatch.Domain.Models;

/// <summary>
/// One "turn it on, walk away, come back" run. The time zone is captured at start so every tick in
/// the session is bucketed into the user's local days, even if the server runs elsewhere.
/// </summary>
public sealed record Session
{
    public required Guid Id { get; init; }
    public required string UserId { get; init; }
    public required DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset? EndedUtc { get; init; }
    public required string TimeZoneId { get; init; }

    public bool IsRunning => EndedUtc is null;

    public TimeZoneInfo TimeZone => TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);

    public static Session Start(Guid id, string userId, DateTimeOffset startedUtc, string timeZoneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out _))
            throw new ArgumentException($"Unknown time zone '{timeZoneId}'.", nameof(timeZoneId));

        return new Session { Id = id, UserId = userId, StartedUtc = startedUtc, TimeZoneId = timeZoneId };
    }

    public Session Stop(DateTimeOffset endedUtc)
    {
        if (!IsRunning)
            throw new InvalidOperationException("The session has already stopped.");
        ArgumentOutOfRangeException.ThrowIfLessThan(endedUtc, StartedUtc);

        return this with { EndedUtc = endedUtc };
    }

    /// <summary>How long the session has run, up to <paramref name="nowUtc"/> if it is still running.</summary>
    public TimeSpan Duration(DateTimeOffset nowUtc) => (EndedUtc ?? nowUtc) - StartedUtc;
}
