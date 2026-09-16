using Microsoft.JSInterop;

namespace PoWatch.Client.Services;

/// <summary>
/// Trim-safe wrapper around <c>window.powatchAudio</c> + <c>window.powatchFx</c>. Every call
/// goes through <see cref="SafeJsInterop"/> so a missing bridge (private mode, polyfill absent)
/// NEVER bubbles a JSException to the kiosk's error UI (audit #10).
///
/// Naming convention: every helper maps 1:1 to a JS method, PascalCased. Returned Tasks
/// discard the bool from <c>TryInvokeVoidAsync</c> — callers simply await or fire-and-forget.
/// </summary>
public sealed class PowatchFxService : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    public PowatchFxService(IJSRuntime js) => _js = js;

    // ─── Audio: pentatonic chimes (#2) ────────────────────────────────────────
    public Task ChimeAsync(string mood, int step) =>
        _js.TryInvokeVoidAsync("powatchAudio.chime", mood, step);

    public Task ArpeggioAsync(string mood, int[] steps) =>
        _js.TryInvokeVoidAsync("powatchAudio.arpeggio", mood, steps);

    // ─── Audio: spatial ping (#5) ─────────────────────────────────────────────
    public Task SpatialPingAsync(string mood, int step, double azimuth) =>
        _js.TryInvokeVoidAsync("powatchAudio.spatialPing", mood, step, azimuth);

    // ─── Audio: breath hush (#7) ──────────────────────────────────────────────
    public Task HushForAsync(int milliseconds) =>
        _js.TryInvokeVoidAsync("powatchAudio.hushFor", milliseconds);

    // ─── Audio: soundscape (#10) ─────────────────────────────────────────────
    public Task StartSoundscapeAsync() => _js.TryInvokeVoidAsync("powatchAudio.startSoundscape");
    public Task SetSoundscapeAsync(double level) => _js.TryInvokeVoidAsync("powatchAudio.setSoundscape", level);
    public Task StopSoundscapeAsync() => _js.TryInvokeVoidAsync("powatchAudio.stopSoundscape");
    public Task SetMutedAsync(bool muted) => _js.TryInvokeVoidAsync("powatchAudio.setMuted", muted);

    // ─── Audio: existing cue surface (preserved) ─────────────────────────────
    public Task CueAsync(string kind) => _js.TryInvokeVoidAsync("powatchAudio.cue", kind);

    // ─── FX: ripples (#3) and parallax (#8) ───────────────────────────────────
    public Task SetupRipplesAsync() => _js.TryInvokeVoidAsync("powatchFx.setupRipples");
    public Task SetupParallaxAsync() => _js.TryInvokeVoidAsync("powatchFx.setupParallax");

    // ─── FX: fireflies (#6) ──────────────────────────────────────────────────
    public Task InitFirefliesAsync() => _js.TryInvokeVoidAsync("powatchFx.fireflies.init");
    public Task SetFireflyIntensityAsync(double intensity) => _js.TryInvokeVoidAsync("powatchFx.fireflies.setIntensity", intensity);
    public Task FireflyBoostAsync(int count) => _js.TryInvokeVoidAsync("powatchFx.fireflies.boost", count);

    // ─── FX: timeline shader (#4) ────────────────────────────────────────────
    public Task InitTimelineAsync(string containerId) => _js.TryInvokeVoidAsync("powatchFx.timeline.init", containerId);
    public Task SetTimelineCountAsync(int count) => _js.TryInvokeVoidAsync("powatchFx.timeline.setCount", count);

    // ─── FX: pebble field (#1) ───────────────────────────────────────────────
    public Task InitPebblesAsync(string containerId) => _js.TryInvokeVoidAsync("powatchFx.pebbles.init", containerId);
    public Task SetPebbleAsync(int index, double x, double y, double r, double hue) =>
        _js.TryInvokeVoidAsync("powatchFx.pebbles.setPebble", index, x, y, r, hue);

    // ─── FX: breath pulse (#7) ───────────────────────────────────────────────
    public Task EnableBreathAsync(bool enabled) => _js.TryInvokeVoidAsync("powatchFx.breath.enable", enabled);
    public Task SetBreathRateAsync(int bpm) => _js.TryInvokeVoidAsync("powatchFx.breath.setRate", bpm);

    // ─── FX: handoff beam (#9) ───────────────────────────────────────────────
    public Task InitHandoffAsync(string containerId) => _js.TryInvokeVoidAsync("powatchFx.handoff.init", containerId);
    public Task StartHandoffBeamAsync(int durationMs) => _js.TryInvokeVoidAsync("powatchFx.handoff.startBeam", durationMs);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
