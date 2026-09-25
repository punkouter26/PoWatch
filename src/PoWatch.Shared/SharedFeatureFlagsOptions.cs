namespace PoWatch.Shared;

/// <summary>
/// Feature flags shared between the server (FeatureFlagsOptions) and the Blazor client
/// (ClientFeatureFlagsOptions). Centralising them here prevents the two classes drifting
/// out of sync — add shared flags here once and both sides inherit automatically.
/// </summary>
public abstract class SharedFeatureFlagsOptions
{
    public bool UseMockAi { get; init; } = false;
    /// <summary>When true, error responses carry exception detail. Never in production.</summary>
    public bool ExposeDebugDetailsInUi { get; init; } = false;
}
