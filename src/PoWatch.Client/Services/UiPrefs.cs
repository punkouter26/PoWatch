using Microsoft.JSInterop;

namespace PoWatch.Client.Services;

/// <summary>
/// The one switch between the simple app and the full one, remembered in this browser. Off by default:
/// no Ask box, downloads, share links, watch rules, weather, CRT tube, comet trails, shader grid or 3D year.
/// </summary>
public sealed class UiPrefs
{
    public const string AdvancedKey = "powatch:advanced";

    public bool Advanced { get; private set; }

    /// <summary>Raised after the stored choice is read and whenever it changes, so open pages re-render.</summary>
    public event Action? Changed;

    public async Task LoadAsync(IJSRuntime js)
    {
        var advanced = await js.TryInvokeAsync<string>("localStorage.getItem", AdvancedKey) == "on";
        if (advanced == Advanced) return;
        Advanced = advanced;
        Changed?.Invoke();
    }

    public async Task SetAdvancedAsync(IJSRuntime js, bool on)
    {
        Advanced = on;
        await js.TryInvokeVoidAsync("localStorage.setItem", AdvancedKey, on ? "on" : "off");
        Changed?.Invoke();
    }
}
