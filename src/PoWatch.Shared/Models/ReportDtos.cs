namespace PoWatch.Shared.Models;

public enum ShiftWindow
{
    FullDay = 0,
    Morning = 1,   // 06:00–14:00 local
    Afternoon = 2, // 14:00–22:00 local
    Night = 3      // 22:00 local → 06:00 local the following morning
}

public sealed class ShiftHandoffReportDto
{
    public DateOnly Date { get; init; }
    public ShiftWindow ShiftWindow { get; init; }

    /// <summary>Start of the covered window, inclusive. Resolved by ShiftClock from local shift hours.</summary>
    public DateTimeOffset WindowStartUtc { get; init; }

    /// <summary>End of the covered window, exclusive.</summary>
    public DateTimeOffset WindowEndUtc { get; init; }
    public string PrimarySubject { get; init; } = string.Empty;
    public string DominantActivity { get; init; } = string.Empty;
    public int TotalEvents { get; init; }
    public int OutlierCount { get; init; }
    public int SignificantCount { get; init; }
    public string ClinicalNarrative { get; init; } = string.Empty;
    public IReadOnlyList<ObservationEventDto> SignificantEvents { get; init; } = [];
    public IReadOnlyList<ObservationEventDto> OutlierEvents { get; init; } = [];
    public DateTimeOffset GeneratedAtUtc { get; init; }
}
