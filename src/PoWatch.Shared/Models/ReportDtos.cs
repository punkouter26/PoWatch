namespace PoWatch.Shared.Models;

public enum ShiftWindow
{
    FullDay = 0,
    Morning = 1,   // 06:00–14:00 local
    Afternoon = 2, // 14:00–22:00 local
    Night = 3      // 22:00 local → 06:00 local the following morning
}
