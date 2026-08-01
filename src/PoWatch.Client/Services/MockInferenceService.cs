namespace PoWatch.Client.Services;

/// <summary>
/// Stand-in inference service, active when <c>FeatureFlags.UseMockAi = true</c>
/// (wwwroot/appsettings.json). Registering it is what raises the persistent MOCK DATA chip, so an
/// operator can never mistake demo output for a real observation.
/// </summary>
internal sealed class MockInferenceService : IInferenceService, IMockable
{
    public string MockLabel => "AI inference";
}
