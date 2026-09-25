namespace PoWatch.Shared.Models;

public sealed class StartSessionRequestDto
{
    /// <summary>Optional client-chosen id, so a retried start returns the same session.</summary>
    public Guid? SessionId { get; init; }

    /// <summary>The browser's IANA time zone; every tick in the session is bucketed into this zone's days.</summary>
    public string TimeZoneId { get; init; } = string.Empty;
}

public sealed class SessionDto
{
    public Guid Id { get; init; }
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset? EndedUtc { get; init; }
    public string TimeZoneId { get; init; } = string.Empty;
    public bool IsRunning { get; init; }
    public double DurationSeconds { get; init; }
}
