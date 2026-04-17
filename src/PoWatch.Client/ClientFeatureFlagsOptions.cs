using PoWatch.Shared;

namespace PoWatch.Client;

/// <summary>
/// Client-side feature flags read from wwwroot/appsettings.json.
/// Shared flags (EnableHud, DriftRadarEnabled, etc.) are inherited from
/// <see cref="SharedFeatureFlagsOptions"/> in PoWatch.Shared.
/// </summary>
public sealed class ClientFeatureFlagsOptions : SharedFeatureFlagsOptions
{
    // Client-only flags below. Shared flags live in SharedFeatureFlagsOptions.

    public int DiagnosticsAutoRefreshIntervalSeconds { get; init; } = 10;

    /// <summary>When true, TTS announces subject state changes.</summary>
    public bool EnableTts { get; init; } = false;

    /// <summary>Max tokens generated per live inference cycle (lower is faster, higher is more detailed).</summary>
    public int MaxInferenceTokens { get; init; } = 96;
}
