using System.Globalization;
using System.Text.RegularExpressions;

namespace PoWatch.Client.Services;

/// <summary>
/// Humanizes machine-generated values for display (UI review items #4–#6): raw model captions
/// ("The image shows a man sitting…") become activity phrases, auto-generated subject ids
/// ("Subject-108") become friendly names, and timestamps carry day context — time-of-day alone
/// is misleading when the data spans weeks.
/// </summary>
public static partial class DisplayText
{
    [GeneratedRegex(@"^Subject-(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex AutoSubjectId();

    [GeneratedRegex(@"^\s*(the\s+(image|photo|photograph|picture|frame)\s+(shows|is\s+a\s+photograph\s+of|is|contains|depicts|appears\s+to\s+show)(\s+that)?|a\s+photograph\s+of|an\s+image\s+of)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex CaptionBoilerplate();

    /// <summary>"Subject-108" → "Person 108" for unnamed subjects; named subjects pass through.</summary>
    public static string SubjectName(string? displayName, bool isKnownIdentity)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return "Unknown person";
        if (isKnownIdentity) return displayName;
        var m = AutoSubjectId().Match(displayName);
        return m.Success ? $"Person {m.Groups[1].Value}" : displayName;
    }

    /// <summary>Strips model-caption boilerplate and capitalizes: "The image shows a man seated…" → "Man seated…".</summary>
    public static string Activity(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "No activity yet";
        var text = CaptionBoilerplate().Replace(raw.Trim(), string.Empty).TrimStart();
        if (text.Length == 0) return raw.Trim();
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    /// <summary>"Today 14:48", "Yesterday 20:24", or "Jul 6, 20:24" — never a bare time-of-day.</summary>
    public static string RelativeTime(DateTimeOffset utc)
    {
        var local = utc.ToLocalTime();
        var today = DateTimeOffset.Now.Date;
        if (local.Date == today) return $"Today {local:HH:mm}";
        if (local.Date == today.AddDays(-1)) return $"Yesterday {local:HH:mm}";
        return local.ToString("MMM d, HH:mm", CultureInfo.CurrentCulture);
    }

    /// <summary>Badges like NEW only earn attention when the activity is actually recent.</summary>
    public static bool IsRecent(DateTimeOffset utc) => DateTimeOffset.UtcNow - utc < TimeSpan.FromHours(24);
}
