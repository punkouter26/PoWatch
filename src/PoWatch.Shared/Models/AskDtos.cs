namespace PoWatch.Shared.Models;

public sealed class AskRequestDto
{
    public string Question { get; init; } = string.Empty;

    /// <summary>The browser's IANA time zone, so "today" means the asker's today.</summary>
    public string? TimeZoneId { get; init; }
}

public sealed class AskAnswerDto
{
    public string Answer { get; init; } = string.Empty;
}

/// <summary>A watch rule fired ("person seen at 23:14"), relayed over the stats hub to every open tab.</summary>
public sealed class AlertDto
{
    public const int MaxLength = 120;

    public DateTimeOffset AtUtc { get; init; }
    public string Text { get; init; } = string.Empty;
}

/// <summary>"Tell me if a person shows up after 10pm": a watch rule in the user's own words.</summary>
public sealed class RuleDraftRequestDto
{
    public const int MaxLength = 200;

    public string Text { get; init; } = string.Empty;
}

/// <summary>A watch rule a model drafted from a sentence; the user still confirms it before it is kept.</summary>
public sealed class RuleDraftDto
{
    /// <summary>A detector label, or "*" for anything.</summary>
    public string Class { get; init; } = string.Empty;
    public int FromHour { get; init; }
    public int ToHour { get; init; }
}
