namespace PoWatch.Application.Options;

public sealed class FeatureFlagsOptions
{
    public bool ObservationLoopEnabled { get; init; } = true;

    public bool SaveSignificantImages { get; init; } = true;

    public bool TtsAnnouncementsEnabled { get; init; } = false;

    public bool ExposeDebugDetailsInUi { get; init; } = false;

    public bool DeveloperBypassAuth { get; init; } = true;
}
