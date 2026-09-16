using PoWatch.Shared;

namespace PoWatch.Client;

/// <summary>
/// Client-side feature flags read from wwwroot/appsettings.json.
/// Shared flags (PollingIntervalSeconds, AlertThresholdsEnabled, etc.) are inherited from
/// <see cref="SharedFeatureFlagsOptions"/> in PoWatch.Shared.
/// </summary>
public sealed class ClientFeatureFlagsOptions : SharedFeatureFlagsOptions
{
    // Client-only flags below. Shared flags live in SharedFeatureFlagsOptions.

    public int DiagnosticsAutoRefreshIntervalSeconds { get; init; } = 10;

    /// <summary>Max tokens generated per live inference cycle (lower is faster, higher is more detailed).</summary>
    public int MaxInferenceTokens { get; init; } = 96;

    /// <summary>Master switch for the 10 visual / audio polish features. Off → no audio cues, no
    /// shader surfaces, no breath hush. Honors <c>prefers-reduced-motion</c> regardless.</summary>
    public bool FxEnabled { get; init; } = true;

    /// <summary>Subset flags. Each one maps to a single improvement number (see plan.md in the
    /// repo root). When <see cref="FxEnabled"/> is false all of these are bypassed too.</summary>
    public bool FxRipple { get; init; } = true;             // #3
    public bool FxPentatonicChime { get; init; } = true;    // #2
    public bool FxParallax { get; init; } = true;           // #8
    public bool FxSoundscape { get; init; } = true;         // #10
    public bool FxFireflies { get; init; } = true;          // #6
    public bool FxTimelineShader { get; init; } = true;     // #4
    public bool FxPebbles { get; init; } = true;            // #1
    public bool FxSpatialAudio { get; init; } = true;       // #5
    public bool FxBreathPulse { get; init; } = true;        // #7
    public bool FxHandoffBeam { get; init; } = true;        // #9
}
