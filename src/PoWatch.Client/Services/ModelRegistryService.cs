using System.Net.Http.Json;

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
/// </para>
/// </summary>
internal sealed class ModelRegistryService(HttpClient http)
{
    private ModelRegistryEntry[]? _cached;

    /// <summary>
    /// The registry entries, fetched once per app load. Throws when the file is unreachable or
    /// malformed — deliberately, because a swallowed failure here is exactly why the model picker
    /// could render blank with no clue why.
    /// </summary>
    public async Task<ModelRegistryEntry[]> GetAsync()
    {
        if (_cached is not null)
            return _cached;

        var entries = await http.GetFromJsonAsync("model-registry.json", PoWatchJsonContext.Default.ModelRegistryEntryArray);
        if (entries is not { Length: > 0 })
            throw new InvalidOperationException("model-registry.json loaded but contained no entries.");

        _cached = entries;
        return entries;
    }
}
