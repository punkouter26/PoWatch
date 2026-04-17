using PoWatch.Shared;

namespace PoWatch.Application.Options;

public sealed class FeatureFlagsOptions : SharedFeatureFlagsOptions
{
    // Server-only flags below. Shared flags live in SharedFeatureFlagsOptions.

    public bool ExposeDebugDetailsInUi { get; init; } = false;

    // Production-safe default is false; enable only via appsettings.Development.json or an explicit override.
    public bool DeveloperBypassAuth { get; init; } = false;

    /// <summary>When true, server-side telemetry sanitization removes prompt leakage and degenerate AI output.</summary>
    public bool EnableTelemetrySanitizer { get; init; } = true;

    /// <summary>When true, the API loads Azure Key Vault configuration and registers the Key Vault health check.</summary>
    public bool EnableKeyVault { get; init; } = false;

    /// <summary>When true, the FHIR R4 Observation export endpoint is available.</summary>
    public bool FhirExportEnabled { get; init; } = true;

    /// <summary>When true, the behavioral baseline and drift scoring background service runs nightly.</summary>
    public bool BaselineEnabled { get; init; } = true;

    /// <summary>When true and AzureOpenAi:Endpoint is configured, the Handoff Coach uses Azure OpenAI for brief generation.</summary>
    public bool AzureOpenAiEnabled { get; init; } = false;
}
