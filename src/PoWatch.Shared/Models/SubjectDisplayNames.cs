using System.Text.RegularExpressions;

namespace PoWatch.Shared.Models;

/// <summary>
/// Turns the auto-generated subject id used in storage ("Subject-116") into the name a caregiver
/// reads ("Person 116"). Named subjects pass through untouched.
/// <para>
/// This lives in PoWatch.Shared, alongside the DTO whose field it formats, because BOTH sides of the
/// BFF boundary need the identical answer: the Blazor client renders it in the people grid, the
/// timeline and the merge dialog, while the server embeds it in the daily narrative and the handoff
/// report. When the client owned the only copy, the server-rendered prose said "Primary subject:
/// Subject-116" on the same screen where every other element said "Person 116". Shared is the one
/// assembly both sides can see, and this helper — like the DTOs around it — depends on nothing.
/// </para>
/// </summary>
public static partial class SubjectDisplayNames
{
    [GeneratedRegex(@"^Subject-(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex AutoSubjectId();

    /// <summary>"Subject-116" → "Person 116"; a named subject is returned unchanged.</summary>
    public static string Humanize(string? displayName, bool isKnownIdentity = false)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return "Unknown person";
        if (isKnownIdentity) return displayName;

        var match = AutoSubjectId().Match(displayName);
        return match.Success ? $"Person {match.Groups[1].Value}" : displayName;
    }
}
