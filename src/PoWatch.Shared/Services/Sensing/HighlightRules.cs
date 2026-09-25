using PoWatch.Shared.Services.Tracking;

namespace PoWatch.Shared.Services.Sensing;

/// <summary>Why a moment deserves a snapshot, and how strongly.</summary>
public sealed record Highlight(string Reason, double Score);

/// <summary>
/// Picks the few moments worth keeping a picture of: the first sighting of each class in a
/// session, a motion spike (at most one per cool-down), and captions that are notable — flagged
/// by the model, or unlike every recent caption. A rolling hourly budget keeps a busy room from
/// turning into a photo stream.
/// </summary>
public sealed class HighlightRules(TimeProvider time, int maxPerHour = 20)
{
    public const double SpikeMotion = 0.15;
    public static readonly TimeSpan SpikeCooldown = TimeSpan.FromMinutes(10);

    /// <summary>How many recent captions a new one is compared with.</summary>
    public const int NoveltyWindow = 10;

    /// <summary>A caption sharing at most this share of content words with each recent one is novel.</summary>
    public const double NoveltyMaxSimilarity = 0.15;

    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<HashSet<string>> _recentCaptions = new();
    private readonly Queue<DateTimeOffset> _kept = new();
    private DateTimeOffset? _lastSpikeUtc;

    public Highlight? OnDetections(TrackerFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var firstNew = frame.Entered.FirstOrDefault(e => !_seen.Contains(e.Label));
        foreach (var entered in frame.Entered) _seen.Add(entered.Label);

        return firstNew is null ? null : Keep(new Highlight($"First {firstNew.Label} of the session", 0.8));
    }

    public Highlight? OnPixel(PixelSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        var now = time.GetUtcNow();
        if (sample.Motion < SpikeMotion || now - _lastSpikeUtc < SpikeCooldown) return null;

        var highlight = Keep(new Highlight("Motion spike", Math.Min(1, sample.Motion * 3)));
        if (highlight is not null) _lastSpikeUtc = now;
        return highlight;
    }

    public Highlight? OnCaption(ParsedCaption caption)
    {
        ArgumentNullException.ThrowIfNull(caption);
        if (caption.Notable) return Keep(new Highlight(caption.Text, 0.9));

        // Small models answer in plain sentences and never flag anything, so novelty stands in: a
        // full window of recent captions, and this one shares almost no content words with any of them.
        var words = ContentWords(caption.Text);
        var novel = words.Count > 0 && _recentCaptions.Count == NoveltyWindow
            && _recentCaptions.All(recent => Jaccard(words, recent) <= NoveltyMaxSimilarity);
        _recentCaptions.Enqueue(words);
        if (_recentCaptions.Count > NoveltyWindow) _recentCaptions.Dequeue();

        return novel ? Keep(new Highlight(caption.Text, 0.7)) : null;
    }

    // ponytail: words of 4+ letters stand in for a stop-word list; CaptionStats (Domain) has the real one.
    private static HashSet<string> ContentWords(string text) =>
        text.ToLowerInvariant().Split((char[])[' ', '.', ',', '!', '?', ';', ':', '"', '\'', '(', ')', '-'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 4).ToHashSet(StringComparer.Ordinal);

    private static double Jaccard(HashSet<string> a, HashSet<string> b)
    {
        var shared = a.Count(b.Contains);
        return (double)shared / (a.Count + b.Count - shared);
    }

    private Highlight? Keep(Highlight highlight)
    {
        var now = time.GetUtcNow();
        while (_kept.Count > 0 && now - _kept.Peek() >= TimeSpan.FromHours(1)) _kept.Dequeue();
        if (_kept.Count >= maxPerHour) return null;

        _kept.Enqueue(now);
        return highlight;
    }
}
