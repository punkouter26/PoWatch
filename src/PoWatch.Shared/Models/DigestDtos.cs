namespace PoWatch.Shared.Models;

/// <summary>One Monday-to-Sunday week, in numbers.</summary>
public sealed class WeekNumbersDto
{
    public DateOnly WeekStart { get; init; }
    public int DaysObserved { get; init; }
    public double SecondsObserved { get; init; }
    public double Occupancy { get; init; }
    public long Visits { get; init; }
    public int PeakConcurrency { get; init; }
    public DateOnly? BusiestDay { get; init; }
    public double BusiestOccupancy { get; init; }
}

/// <summary>A week against the one before it: the numbers, a paragraph and the newcomers.</summary>
public sealed class WeekDigestDto
{
    public WeekNumbersDto ThisWeek { get; init; } = new();
    public WeekNumbersDto LastWeek { get; init; } = new();

    /// <summary>Regulars first seen this week, by their display names.</summary>
    public List<string> NewRegulars { get; init; } = [];
    public string Summary { get; init; } = string.Empty;

    /// <summary>A model's one-line title for the week, kept only when it uses no number the summary lacks; else null.</summary>
    public string? Headline { get; init; }
}
