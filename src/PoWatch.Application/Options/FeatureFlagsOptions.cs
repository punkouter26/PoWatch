namespace PoWatch.Application.Options;

public sealed class FeatureFlagsOptions
{
    public bool ObservationLoopEnabled { get; init; } = true;

    public bool SaveSignificantImages { get; init; } = true;

    public bool TtsAnnouncementsEnabled { get; init; } = false;

    public bool ExposeDebugDetailsInUi { get; init; } = false;

    // Production-safe default is false; enable only via appsettings.Development.json or an explicit override.
    public bool DeveloperBypassAuth { get; init; } = false;

    /// <summary>When true the client uses a mock inference response instead of real WebGPU/Gemma.</summary>
    public bool UseMockAi { get; init; } = false;

    /// <summary>Polling interval in seconds between webcam analysis cycles.</summary>
    public int PollingIntervalSeconds { get; init; } = 10;

    /// <summary>Maximum number of telemetry records to display in the UI history table.</summary>
    public int MaxHistoryRows { get; init; } = 50;

    /// <summary>When true, the HUD overlay shows real-time inference metrics.</summary>
    public bool EnableHud { get; init; } = true;

    /// <summary>When true, server-side telemetry sanitization removes prompt leakage and degenerate AI output.</summary>
    public bool EnableTelemetrySanitizer { get; init; } = true;

    /// <summary>When true, the API loads Azure Key Vault configuration and registers the Key Vault health check.</summary>
    public bool EnableKeyVault { get; init; } = false;

    /// <summary>When true, the FHIR R4 Observation export endpoint is available.</summary>
    public bool FhirExportEnabled { get; init; } = true;

    /// <summary>When true, the behavioral baseline and drift scoring background service runs nightly.</summary>
    public bool BaselineEnabled { get; init; } = true;

    /// <summary>When true, the alert threshold evaluation engine fires during observation ingest.</summary>
    public bool AlertThresholdsEnabled { get; init; } = true;

    /// <summary>When true, the Drift Radar feature computes per-subject behavioral drift on the live-risk endpoint.</summary>
    public bool DriftRadarEnabled { get; init; } = true;

    /// <summary>When true, the Handoff Coach endpoint is available to generate AI-assisted or template-based briefs.</summary>
    public bool HandoffCoachEnabled { get; init; } = true;

    /// <summary>When true and AzureOpenAi:Endpoint is configured, the Handoff Coach uses Azure OpenAI for brief generation.</summary>
    public bool AzureOpenAiEnabled { get; init; } = false;
}
