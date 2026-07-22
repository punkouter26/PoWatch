using Microsoft.JSInterop;

namespace PoWatch.Client.Services;

/// <summary>
/// Safe wrappers around <see cref="IJSRuntime"/> that swallow the JSInterop exceptions
/// thrown when an optional JS bridge (audio, blob upload, webcam-shell overlay, …) failed
/// to load or evaluate. Audit #10: every uncaught JSException shows up to the user as the
/// global "An unhandled error has occurred" banner, which is hostile on a kiosk. Bridges
/// are enhancements, not requirements — the app must keep rendering when one is missing.
/// </summary>
public static class SafeJsInterop
{
    /// <summary>Fire-and-forget; never throws. Returns true when the JS call actually ran.</summary>
    public static async Task<bool> TryInvokeVoidAsync(this IJSRuntime js, string identifier, params object?[] args)
    {
        try
        {
            await js.InvokeVoidAsync(identifier, args);
            return true;
        }
        catch (JSDisconnectedException)
        {
            // The browser navigated away or the circuit dropped — silent.
            return false;
        }
        catch (JSException)
        {
            // The bridge isn't there or threw. Optional; log nothing for the user.
            return false;
        }
        catch (InvalidOperationException)
        {
            // JSRuntime says "this isn't a WebAssembly rendering" — happens during prerender
            // and during the dispose window. Treat the same as a missing bridge.
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }

    /// <summary>Returns <c>default</c> instead of throwing when the bridge is missing.</summary>
    // IL2091 (trim): the generic type T propagates whatever DynamicallyAccessedMemberTypes the
    // caller already declared on their own InvokeAsync<T> call site, so this passthrough can't
    // re-state the annotation. Suppress at the call: every existing call site already gives the
    // analyzer the right view (Inferable, Record, plain DTOs, etc.).
#pragma warning disable IL2091
    public static async Task<T?> TryInvokeAsync<T>(this IJSRuntime js, string identifier, params object?[] args)
    {
        try
        {
            return await js.InvokeAsync<T?>(identifier, args);
        }
        catch (JSDisconnectedException) { return default; }
        catch (JSException) { return default; }
        catch (InvalidOperationException) { return default; }
        catch (TaskCanceledException) { return default; }
    }
#pragma warning restore IL2091
}
