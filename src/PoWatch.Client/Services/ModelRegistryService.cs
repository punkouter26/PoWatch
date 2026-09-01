using System.Net.Http.Json;
using Microsoft.JSInterop;

namespace PoWatch.Client.Services;

/// <summary>
/// Reads <c>wwwroot/model-registry.json</c> — the single source of truth for the VLM list (rule 1.5),
/// shared verbatim with <c>inference-worker.js</c>.
/// <para>
/// Two pages need the list: the Live Room's model picker and the System page's per-model self-test.
/// They fetch it through here rather than each carrying its own loader, so the trim-safe
/// source-generated deserialization and the fetch-once cache stay in one place. Callers keep their
/// own failure copy: the picker falls back to a single option so it never renders blank, while the
/// self-test card has nothing useful to show and says so instead.
/// <para>
/// Resilience: a successful registry is mirrored into <c>sessionStorage</c>, and a failed fetch is
/// retried once after a short back-off before surfacing. The registry is a static file, so any copy
/// fetched earlier in this browser session is exactly as good as a fresh 200 — a transient network
/// blip no longer blanks the model picker for the whole session.
/// </para>
/// </summary>
internal sealed class ModelRegistryService(HttpClient http, IJSRuntime js)
{
    private const string SessionStorageKey = "powatch.model-registry";
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(600);

    private ModelRegistryEntry[]? _cached;

    /// <summary>
    /// The registry entries: in-memory cache → sessionStorage from an earlier load in this tab →
    /// network (with one retry). Throws only when every path failed — deliberately, because a
    /// swallowed failure here is exactly why the model picker could render blank with no clue why.
    /// </summary>
    public async Task<ModelRegistryEntry[]> GetAsync()
    {
        if (_cached is not null)
            return _cached;

        var restored = await TryRestoreFromSessionStorageAsync();
        if (restored is { Length: > 0 })
        {
            _cached = restored;
            return _cached;
        }

        var entries = await FetchWithRetryAsync();

        if (entries is not { Length: > 0 })
            throw new InvalidOperationException("model-registry.json loaded but contained no entries.");

        _cached = entries;
        await TryPersistToSessionStorageAsync(entries);
        return _cached;
    }

    private async Task<ModelRegistryEntry[]?> FetchWithRetryAsync()
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(RetryDelay);
            try
            {
                return await http.GetFromJsonAsync("model-registry.json", PoWatchJsonContext.Default.ModelRegistryEntryArray);
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or System.Text.Json.JsonException)
            {
                lastError = ex;
            }
        }

        throw lastError ?? new InvalidOperationException("model-registry.json could not be loaded.");
    }

    private async Task<ModelRegistryEntry[]?> TryRestoreFromSessionStorageAsync()
    {
        try
        {
            var json = await js.InvokeAsync<string?>("sessionStorage.getItem", SessionStorageKey);
            if (string.IsNullOrWhiteSpace(json))
                return null;
            return System.Text.Json.JsonSerializer.Deserialize(json, PoWatchJsonContext.Default.ModelRegistryEntryArray);
        }
        catch
        {
            return null; // Storage unavailable (privacy mode) or corrupt entry — just refetch.
        }
    }

    private async Task TryPersistToSessionStorageAsync(ModelRegistryEntry[] entries)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(entries, PoWatchJsonContext.Default.ModelRegistryEntryArray);
            await js.InvokeVoidAsync("sessionStorage.setItem", SessionStorageKey, json);
        }
        catch
        {
            // Best-effort persistence only.
        }
    }
}
