namespace PoWatch.Client;

/// <summary>Typed options for client-side feature flags read from wwwroot/appsettings.json.</summary>
public sealed class ClientFeatureFlagsOptions
{
    public bool ObservationLoopEnabled { get; init; } = true;
    public bool SaveSignificantImages { get; init; } = true;
    public bool TtsAnnouncementsEnabled { get; init; } = false;
    public int DiagnosticsAutoRefreshIntervalSeconds { get; init; } = 10;
}
