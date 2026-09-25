using System.Globalization;
using System.Text.RegularExpressions;
using PoWatch.Shared.Models;

namespace PoWatch.Shared.Services.Recaps;

/// <summary>
/// The one prompt any model — the server's or the browser's built-in one — gets for rewriting a recap
/// paragraph, and the check that its answer kept to the facts. The numbers stay the template's.
/// </summary>
public static partial class RecapPrompt
{
    public const string System =
        "You write short, upbeat recaps for a hobbyist who points a webcam at a room and loves statistics. " +
        "Use only the facts given. Three or four sentences, no lists, no invented numbers, no safety or medical language.";

    public static string User(RecapDto recap)
    {
        ArgumentNullException.ThrowIfNull(recap);
        return $"{recap.Title} ({recap.Subtitle}).\nFacts: {recap.Summary}\n{string.Join('\n', recap.Highlights)}\n" +
               $"Numbers: {string.Join("; ", recap.Numbers.Select(n => $"{n.Label} {n.Value}"))}";
    }

    /// <summary>
    /// True when every number in <paramref name="text"/> also appears in <paramref name="facts"/>.
    /// "No invented numbers" is otherwise only a request; this makes it a rule.
    /// </summary>
    public static bool KeepsToFacts(string text, string facts)
    {
        var allowed = Numbers(facts).ToHashSet();
        return Numbers(text).All(allowed.Contains);
    }

    /// <summary>The template recap with a model's paragraph in place of its own.</summary>
    public static RecapDto Rewritten(RecapDto recap, string summary, string source)
    {
        ArgumentNullException.ThrowIfNull(recap);
        return new RecapDto
        {
            Title = recap.Title,
            Subtitle = recap.Subtitle,
            FromUtc = recap.FromUtc,
            ToUtc = recap.ToUtc,
            Summary = summary,
            Highlights = recap.Highlights,
            Numbers = recap.Numbers,
            Moments = recap.Moments,
            Source = source
        };
    }

    // "1,234" and "1234", "05" and "5", "0.50" and "0.5" are the same number.
    private static IEnumerable<double> Numbers(string text) =>
        NumberPattern().Matches(text ?? string.Empty)
            .Select(m => double.Parse(m.Value.Replace(",", string.Empty, StringComparison.Ordinal), CultureInfo.InvariantCulture));

    [GeneratedRegex(@"\d+(?:,\d{3})*(?:\.\d+)?")]
    private static partial Regex NumberPattern();
}
