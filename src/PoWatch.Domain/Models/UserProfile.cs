namespace PoWatch.Domain.Models;

/// <summary>A place picked for weather: a named point and the time zone its days are counted in.</summary>
public sealed record Place(string Name, double Latitude, double Longitude, string? TimeZoneId);

/// <summary>
/// The few choices a user makes that the server has to act on while nobody is looking: how long the
/// raw record is kept, where the weather comes from, and whether a weekly digest is mailed.
/// </summary>
public sealed record UserProfile
{
    /// <summary>The only retention windows on offer, in days; none (null) keeps everything.</summary>
    public static readonly IReadOnlyList<int> RetentionChoices = [30, 90, 365];

    public required string UserId { get; init; }

    /// <summary>The zone the user last saved from: "Monday morning" and "older than N days" are counted in it.</summary>
    public string? TimeZoneId { get; init; }

    /// <summary>Days of raw ticks, events and snapshots to keep; null keeps everything. Rollups are always kept.</summary>
    public int? RetentionDays { get; init; }

    public Place? Place { get; init; }

    public bool DigestEmail { get; init; }

    /// <summary>Where the digest goes: the address the sign-in carried when the user opted in.</summary>
    public string? Email { get; init; }

    /// <summary>Monday of the last week whose digest was mailed, so a restart never mails it twice.</summary>
    public DateOnly? LastDigestWeek { get; init; }
}
