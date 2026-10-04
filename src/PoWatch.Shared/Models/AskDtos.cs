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
