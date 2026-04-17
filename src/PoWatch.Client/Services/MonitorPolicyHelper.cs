namespace PoWatch.Client.Services;

/// <summary>
/// Encapsulates monitor-specific heuristics so the page focuses on rendering and wiring.
/// </summary>
internal static class MonitorPolicyHelper
{
    public static string ExtractSubjectId(string description)
    {
        if (string.IsNullOrEmpty(description))
            return string.Empty;

        var match = System.Text.RegularExpressions.Regex.Match(
            description,
            @"<SUBJECT:([^>]+)>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }

    /// <summary>
    /// Determines whether a new observation is worth surfacing to the user.
    ///   1. Subject disappears — previous had a subject, current does not (room became empty).
    ///   2. Subject identity change — different SubjectId tag present.
    ///   3. Semantic keyword trigger — high-signal action words (entry/exit, posture, count change).
    ///   4. Jaccard word-token distance &gt; 0.5 — content diverged significantly.
    /// </summary>
    public static bool IsSignificantChange(
        string currentDescription,
        string currentSubjectId,
        string previousDescription,
        string previousSubjectId)
    {
        if (string.IsNullOrWhiteSpace(previousDescription))
            return true;

        if (!string.IsNullOrWhiteSpace(previousSubjectId) && string.IsNullOrWhiteSpace(currentSubjectId))
            return true;

        if (!string.IsNullOrWhiteSpace(currentSubjectId)
            && !string.Equals(currentSubjectId, previousSubjectId, StringComparison.Ordinal))
            return true;

        if (ContainsSignificantKeyword(currentDescription))
            return true;

        return JaccardDistance(currentDescription, previousDescription) > 0.5;
    }

    private static readonly string[] SignificantKeywords =
    [
        "enter", "leav", "exit", "walk", "run", "stand", "sit", "lie",
        "fall", "empty", "nobody", "no one", "alone"
    ];

    private static bool ContainsSignificantKeyword(string text)
    {
        foreach (var kw in SignificantKeywords)
        {
            if (text.Contains(kw, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static double JaccardDistance(string a, string b)
    {
        var setA = Tokenize(a);
        var setB = Tokenize(b);

        if (setA.Count == 0 && setB.Count == 0) return 0.0;

        var intersection = setA.Intersect(setB, StringComparer.OrdinalIgnoreCase).Count();
        var union = setA.Union(setB, StringComparer.OrdinalIgnoreCase).Count();

        return union == 0 ? 0.0 : 1.0 - (double)intersection / union;
    }

    private static HashSet<string> Tokenize(string text) =>
        [.. System.Text.RegularExpressions.Regex.Matches(text, @"\b[a-zA-Z]{3,}\b")
            .Select(m => m.Value)];
}
