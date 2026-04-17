namespace PoWatch.Client;

/// <summary>Typed options for client-side feature flags read from wwwroot/appsettings.json.</summary>
public sealed class ClientFeatureFlagsOptions
{
    public bool ObservationLoopEnabled { get; init; } = true;
    public bool SaveSignificantImages { get; init; } = true;
    public bool TtsAnnouncementsEnabled { get; init; } = false;
    public int DiagnosticsAutoRefreshIntervalSeconds { get; init; } = 10;

    /// <summary>When true the client uses a mock inference response instead of real WebGPU/Gemma.</summary>
    public bool UseMockAi { get; init; } = false;

    /// <summary>Polling interval in seconds between webcam analysis cycles.</summary>
    public int PollingIntervalSeconds { get; init; } = 10;

    /// <summary>Maximum number of telemetry records to display in the UI history table.</summary>
    public int MaxHistoryRows { get; init; } = 50;

    /// <summary>When true, the HUD overlay shows real-time inference metrics.</summary>
    public bool EnableHud { get; init; } = true;

    /// <summary>When true, TTS announces subject state changes.</summary>
    public bool EnableTts { get; init; } = false;

    /// <summary>When true, the alert threshold banner is shown in ObserverHub.</summary>
    public bool AlertThresholdsEnabled { get; init; } = true;

    /// <summary>Max tokens generated per live inference cycle (lower is faster, higher is more detailed).</summary>
    public int MaxInferenceTokens { get; init; } = 96;

    /// <summary>When true, Drift Radar badges and the subject drift panel are shown in Live Dashboard.</summary>
    public bool DriftRadarEnabled { get; init; } = true;

    /// <summary>When true, the Handoff Coach brief generator is shown in Archives.</summary>
    public bool HandoffCoachEnabled { get; init; } = true;
}
