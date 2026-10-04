using System.Text.Json;
using Microsoft.JSInterop;

namespace PoWatch.Client.Services;

/// <summary>"Tell me when a <see cref="Class"/> appears between these local hours." A window may wrap midnight (22 → 6).</summary>
/// <param name="Class">A detector label, or <see cref="WatchRules.Any"/>.</param>
public sealed record WatchRule(string Class, int FromHour, int ToHour)
{
    public bool Matches(string label, DateTimeOffset localNow)
    {
        if (Class != WatchRules.Any && !string.Equals(Class, label, StringComparison.OrdinalIgnoreCase)) return false;
        var hour = localNow.Hour;
        return FromHour == ToHour
            || (FromHour < ToHour ? hour >= FromHour && hour < ToHour : hour >= FromHour || hour < ToHour);
    }

    public override string ToString() =>
        $"{(Class == WatchRules.Any ? "anything" : Class)} · {(FromHour == ToHour ? "any time" : $"{FromHour:00}:00–{ToHour:00}:00")}";
}

/// <summary>The user's watch rules, kept in this browser (they describe this camera, not the account).</summary>
public static class WatchRules
{
    public const string Any = "*";
    public const int Max = 8;
    private const string Key = "powatch:rules";

    public static async Task<List<WatchRule>> LoadAsync(IJSRuntime js)
    {
        try
        {
            var json = await js.TryInvokeAsync<string>("localStorage.getItem", Key);
            return string.IsNullOrEmpty(json) ? [] : JsonSerializer.Deserialize(json, PoWatchJsonContext.Default.ListWatchRule) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static Task SaveAsync(IJSRuntime js, List<WatchRule> rules) =>
        js.TryInvokeVoidAsync("localStorage.setItem", Key, JsonSerializer.Serialize(rules, PoWatchJsonContext.Default.ListWatchRule));
}
