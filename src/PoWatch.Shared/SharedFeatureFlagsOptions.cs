namespace PoWatch.Shared;

/// <summary>
/// Feature flags shared between the server (FeatureFlagsOptions) and the Blazor client
/// (ClientFeatureFlagsOptions). Centralising them here prevents the two classes drifting
/// out of sync — add shared flags here once and both sides inherit automatically.
/// </summary>
public abstract class SharedFeatureFlagsOptions
{
    public bool ObservationLoopEnabled { get; init; } = true;
    public bool SaveSignificantImages { get; init; } = true;
    public bool UseMockAi { get; init; } = false;
    public int PollingIntervalSeconds { get; init; } = 10;
    public int MaxHistoryRows { get; init; } = 50;
    public bool EnableHud { get; init; } = false;
    public bool AlertThresholdsEnabled { get; init; } = true;
    public bool DriftRadarEnabled { get; init; } = true;
    public bool HandoffCoachEnabled { get; init; } = true;
    /// <summary>
    /// Minimum number of observations before the TEMP badge is shown on Live Dashboard cards.
    /// Set to 0 (default) to always show TEMP for unknown subjects.
    /// </summary>
    public int TempBadgeMinObservations { get; init; } = 0;
    /// <summary>When true, debug-only sections (e.g. Danger Zone on Diagnostics) are visible in the UI.</summary>
    public bool ExposeDebugDetailsInUi { get; init; } = false;
}
