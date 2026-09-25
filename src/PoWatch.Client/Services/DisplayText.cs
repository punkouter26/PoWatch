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

    /// <summary>"1h 02m", "4m 05s" or "12s" — one way to print a length of time on every page.</summary>
    public static string Duration(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours}h {span.Minutes:00}m"
            : span.TotalMinutes >= 1 ? $"{span.Minutes}m {span.Seconds:00}s"
            : $"{span.Seconds}s";
    }

    /// <summary>The short session tag shown everywhere: "#AB12".</summary>
    public static string SessionTag(Guid id) => $"#{id.ToString("N")[..4].ToUpperInvariant()}";
}
