using System.Text;

namespace PoWatch.Domain.Services;

/// <summary>Generates consistent URL-safe subject IDs from display names.</summary>
public static class SubjectIdSlugger
{
    /// <summary>
    /// Returns the canonical subject ID for a rename or merge operation.
    /// Preserves the current ID when it is already a named identity (not a generated Subject-N slot).
    /// </summary>
    public static string ResolveCanonicalSubjectId(string currentSubjectId, string displayName)
    {
        if (!currentSubjectId.StartsWith("Subject-", StringComparison.OrdinalIgnoreCase)
            || string.Equals(currentSubjectId, displayName, StringComparison.OrdinalIgnoreCase))
        {
            return currentSubjectId;
        }

        return BuildCanonicalSubjectId(displayName);
    }

    /// <summary>Converts a display name to a lowercase URL-safe slug used as the subject storage key.</summary>
    public static string BuildCanonicalSubjectId(string displayName)
    {
        var builder = new StringBuilder();

        foreach (var character in displayName.Trim().ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '-');
        }

        return builder.ToString().Trim('-');
    }
}
