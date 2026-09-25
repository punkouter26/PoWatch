using System.Globalization;

namespace PoWatch.Client.Services;

/// <summary>Timestamps with day context: time-of-day alone is misleading when the data spans weeks.</summary>
public static class DisplayText
{
    /// <summary>"Today 14:48", "Yesterday 20:24", or "Jul 6, 20:24" — never a bare time-of-day.</summary>
    public static string RelativeTime(DateTimeOffset utc)
    {
        var local = utc.ToLocalTime();
        var today = DateTimeOffset.Now.Date;
        if (local.Date == today) return $"Today {local:HH:mm}";
        if (local.Date == today.AddDays(-1)) return $"Yesterday {local:HH:mm}";
        return local.ToString("MMM d, HH:mm", CultureInfo.CurrentCulture);
    }
}
