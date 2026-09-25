namespace PoWatch.Shared.Models;

public sealed class AchievementDto
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public DateTimeOffset? UnlockedAtUtc { get; init; }
    public bool Unlocked => UnlockedAtUtc is not null;
}

public sealed class RecordDto
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public double Value { get; init; }

    /// <summary>The value as a person would say it, e.g. "3 hours, 12 minutes" or "41 visits".</summary>
    public string Display { get; init; } = string.Empty;
    public DateTimeOffset SetAtUtc { get; init; }
    public double? PreviousValue { get; init; }
}

/// <summary>Everything on /trophies: every achievement (locked ones too) and every personal record set so far.</summary>
public sealed class TrophyCabinetDto
{
    public List<AchievementDto> Achievements { get; init; } = [];
    public List<RecordDto> Records { get; init; } = [];
}

/// <summary>Pushed on the stats hub when achievements unlock, so the header can show a toast.</summary>
public sealed class AchievementsUnlockedDto
{
    public List<AchievementDto> Unlocked { get; init; } = [];
}
