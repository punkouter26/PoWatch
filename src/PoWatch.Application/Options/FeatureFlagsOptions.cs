namespace PoWatch.Application.Options;

public sealed class FeatureFlagsOptions
{
    /// <summary>When true, error responses carry exception detail. Never in production.</summary>
    public bool ExposeDebugDetailsInUi { get; init; }

    /// <summary>Guest sign-in (FakeAuth). Dev/Test only; refused in Production.</summary>
    public bool DeveloperBypassAuth { get; init; }

    /// <summary>When true, the API loads Azure Key Vault configuration and registers the Key Vault health check.</summary>
    public bool EnableKeyVault { get; init; }
}
