namespace PoWatch.Domain.Services;

public sealed record WordCount(string Word, int Count);

/// <summary>Stat family D — what the vision model said.</summary>
public static class CaptionStats
{
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "and", "or", "but", "of", "to", "in", "on", "at", "by", "for", "with", "from",
        "onto", "into", "is", "are", "was", "were", "be", "been", "being", "it", "its", "this", "that",
        "there", "their", "they", "them", "he", "she", "his", "her", "who", "which", "while", "some",
        "image", "picture", "photo", "shows", "appears", "seems", "can", "seen", "visible"
    };

    /// <summary>The most common content words across captions; ties break alphabetically.</summary>
    public static IReadOnlyList<WordCount> WordFrequencies(IEnumerable<string> captions, int top)
    {
        ArgumentNullException.ThrowIfNull(captions);

        return captions
            .SelectMany(Tokens)
            .Where(IsContentWord)
            .GroupBy(w => w, StringComparer.Ordinal)
            .Select(g => new WordCount(g.Key, g.Count()))
            .OrderByDescending(w => w.Count)
            .ThenBy(w => w.Word, StringComparer.Ordinal)
            .Take(top)
            .ToList();
    }

    /// <summary>
    /// The caption least like the others — lowest mean Jaccard similarity of content words.
    /// Needs at least three captions to mean anything.
    /// </summary>
    public static (DateTimeOffset AtUtc, string Text)? Weirdest(IReadOnlyList<(DateTimeOffset AtUtc, string Text)> captions)
    {
        ArgumentNullException.ThrowIfNull(captions);
        if (captions.Count < 3) return null;

        var sets = captions.Select(c => ContentSet(c.Text)).ToList();
        var weirdest = Enumerable.Range(0, captions.Count)
            .MinBy(i => Enumerable.Range(0, captions.Count).Where(j => j != i).Average(j => Jaccard(sets[i], sets[j])));

        return captions[weirdest];
    }

    /// <summary>Jaccard similarity of two captions' content words, in [0, 1].</summary>
    public static double Jaccard(string a, string b) => Jaccard(ContentSet(a), ContentSet(b));

    private static double Jaccard(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 && b.Count == 0) return 1;
        var intersection = a.Count(b.Contains);
        return (double)intersection / (a.Count + b.Count - intersection);
    }

    private static HashSet<string> ContentSet(string text) =>
        Tokens(text).Where(IsContentWord).ToHashSet(StringComparer.Ordinal);

    private static bool IsContentWord(string token) => token.Length >= 3 && !StopWords.Contains(token);

    private static IEnumerable<string> Tokens(string text) =>
        (text ?? string.Empty)
            .ToLowerInvariant()
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static readonly char[] Separators = [' ', '.', ',', '!', '?', ';', ':', '"', '\'', '(', ')', '-', '\n', '\r', '\t'];
}
