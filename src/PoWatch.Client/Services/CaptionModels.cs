namespace PoWatch.Client.Services;

/// <summary>
/// The caption models Settings offers, keyed as in wwwroot/js/caption-models.js. The choice is kept
/// per browser: it depends on this device's GPU, not on the account.
/// </summary>
public static class CaptionModels
{
    public const string Key = "powatch:caption-model";

    /// <summary>
    /// Key and the label Settings shows, default first. Florence-2 leads on SCRIPTS/caption-eval (closest to
    /// human captions, none rejected, quickest to load); SmolVLM 500M is the one that reads the detector's hint.
    /// </summary>
    public static readonly IReadOnlyList<(string Key, string Label)> All =
    [
        ("florence-2", "FLORENCE-2"),
        ("smolvlm-500m", "SMOLVLM 500M · USES DETECTOR"),
        ("smolvlm-256m", "SMOLVLM 256M"),
    ];

    public static string Default => All[0].Key;

    public static string Label(string key) => All.FirstOrDefault(m => m.Key == key).Label ?? All[0].Label;

    public static string Normalize(string? key) => All.Any(m => m.Key == key) ? key! : Default;

    public static string Next(string key)
    {
        var i = All.Select(m => m.Key).ToList().IndexOf(key);
        return All[(i + 1) % All.Count].Key;
    }
}
