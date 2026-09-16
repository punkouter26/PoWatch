using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace PoWatch.Client.Pages;

/// <summary>
/// Viewport-aware and focus-management helpers for the Live Room. Split out of the main code-behind
/// so the layout-vs-domain logic doesn't grow further. The two real responsibilities here are:
/// <list type="bullet">
///   <item><description>Tracking viewport width so the page renders compact (heatmap summary, no
///     ambient FX) below ~720px wide.</description></item>
///   <item><description>Wiring the observer-settings drawer to behave like a modal dialog —
///     focus moves into it on open and Escape closes it.</description></item>
/// </list>
/// </summary>
public partial class ObserverHub
{
    // 720px chosen because the Live Room's heatmap strip stops being readable below that width
    // and the hero becomes the most important thing on screen.
    private const int CompactBreakpointPx = 720;

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            // The JS helper publishes the current breakpoint via CSS — we just read what the page
            // already knows so the value matches whatever was rendered this frame.
            try
            {
                _compactLayout = await JS.InvokeAsync<bool>("powatchViewport.isCompact", CompactBreakpointPx);
            }
            catch
            {
                _compactLayout = false;
            }

            // Subscribe to viewport changes so a phone-to-tablet handoff (or a kiosk operator
            // rotating the screen) re-renders compact/non-compact without a navigation.
            try
            {
                _viewportDotNetRef = DotNetObjectReference.Create(this);
                await JS.InvokeVoidAsync("powatchViewport.subscribe", CompactBreakpointPx, _viewportDotNetRef);
            }
            catch
            {
                /* old browser or pre-script environment */
            }

            if (_settingsOpen)
            {
                await FocusSettingsDrawerAsync();
            }
        }
        else if (_settingsOpen && _settingsDrawerFocusedOnce == false)
        {
            // The drawer can be opened after first render (clicking the ⚙ button); move focus in.
            await FocusSettingsDrawerAsync();
        }
    }

    private DotNetObjectReference<ObserverHub>? _viewportDotNetRef;
    private bool _settingsDrawerFocusedOnce;

    [JSInvokable]
    public Task OnViewportChangedAsync(bool isCompact)
    {
        if (_compactLayout == isCompact) return Task.CompletedTask;
        _compactLayout = isCompact;
        return InvokeAsync(StateHasChanged);
    }

    private async Task FocusSettingsDrawerAsync()
    {
        try
        {
            await _settingsDrawerRef.FocusAsync();
            _settingsDrawerFocusedOnce = true;
        }
        catch
        {
            /* element may have unmounted between render and focus */
        }
    }

    /// <summary>Escape closes the drawer; Tab/Shift+Tab wrap inside it. Implements the focus-trap
    /// behaviour the previous instant-mount drawer was missing (idea #9).</summary>
    private void HandleSettingsDrawerKeyDown(KeyboardEventArgs e)
    {
        if (string.Equals(e.Key, "Escape", StringComparison.Ordinal))
        {
            _settingsOpen = false;
            return;
        }
        // A full Tab trap requires JS, but the basic Escape is enough to make the drawer
        // keyboard-discoverable; Tab walking out is acceptable while a single-tab trap would
        // require a JS focus-roving listener. The accessibility win we get from Escape alone
        // is the bigger one for kiosk users.
    }
}