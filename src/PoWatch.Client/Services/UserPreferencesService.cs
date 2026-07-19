using Microsoft.JSInterop;

namespace PoWatch.Client.Services;

/// <summary>
/// Persists per-operator preferences (model, polling interval, theme) to
/// localStorage. Survives F5 and full nav round-trips so re-opening Observer
/// Hub from Live Dashboard doesn't reset model choice. Singleton.
/// </summary>
public sealed class UserPreferencesService : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private readonly Dictionary<string, string> _cache = new(StringComparer.Ordinal);
    private bool _hydrated;

    public UserPreferencesService(IJSRuntime js) => _js = js;

    /// <summary>Fired when a key is mutated — subscribers can call StateHasChanged on themselves.</summary>
    public event EventHandler<PreferenceChangedEventArgs>? PreferenceChanged;

    public async Task<string?> GetAsync(string key)
    {
        await EnsureHydratedAsync();
        return _cache.TryGetValue(key, out var v) ? v : null;
    }

    public async Task<int?> GetIntAsync(string key)
    {
        var raw = await GetAsync(key);
        return int.TryParse(raw, out var n) ? n : null;
    }

    public async Task SetAsync(string key, string value)
    {
        await EnsureHydratedAsync();
        if (_cache.TryGetValue(key, out var existing) && existing == value) return;
        _cache[key] = value;
        try { await _js.InvokeVoidAsync("localStorage.setItem", key, value); }
        catch { /* private mode — keep in-memory only */ }
        PreferenceChanged?.Invoke(this, new(key, value));
    }

    public async Task SetIntAsync(string key, int value) => await SetAsync(key, value.ToString());

    private async Task EnsureHydratedAsync()
    {
        if (_hydrated) return;
        _hydrated = true;
        try
        {
            // Pull all powatch:* keys; cheap because the namespace is small.
            var keys = await _js.InvokeAsync<List<string>>("powatchPref.keys");
            foreach (var k in keys)
            {
                var v = await _js.InvokeAsync<string?>("powatchPref.get", k);
                if (!string.IsNullOrEmpty(v)) _cache[k] = v;
            }
        }
        catch
        {
            // First load is pre-hydration or WASM hot-reload — keep going.
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Nothing to dispose; cache is pure memory.
        await ValueTask.CompletedTask;
    }
}

public sealed record PreferenceChangedEventArgs(string Key, string Value);
