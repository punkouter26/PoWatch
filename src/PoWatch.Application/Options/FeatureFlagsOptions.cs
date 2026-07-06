using PoWatch.Shared;

namespace PoWatch.Application.Options;

public sealed class FeatureFlagsOptions : SharedFeatureFlagsOptions
{
    // Server-only flags below. Shared flags live in SharedFeatureFlagsOptions.

    // NOTE: ExposeDebugDetailsInUi is in SharedFeatureFlagsOptions (shared with client).

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

    /// <summary>When true, the POST /api/diagnostics/reset endpoint is active. NEVER enable in production.</summary>
    public bool AllowDataReset { get; init; } = false;

    /// <summary>
    /// When true (Dev/Test only — never honoured in Production even if set), the login page renders the
    /// "Sign in with Microsoft" button. In Dev, this lets the operator see the split-view UI per
    /// NET_RULE §4.4 without committing a real AzureAd:ClientId to source. The actual
    /// /auth/login/microsoft endpoint still requires a configured ClientId and returns 404 otherwise,
    /// so dev sign-in remains honest: clicking Microsoft without a real Azure AD app registration
    /// surfaces a clear "Microsoft sign-in is not configured" message from the API.
    /// </summary>
    public bool DeveloperEnableMicrosoftLogin { get; init; } = false;
}
