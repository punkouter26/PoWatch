namespace PoWatch.Application.Models;

public sealed class ObserverRuntimeState
{
    public required bool ObservationLoopEnabled { get; init; }

    public required bool TtsAnnouncementsEnabled { get; init; }

    public required bool SaveSignificantImages { get; init; }

    public required int PollIntervalSeconds { get; init; }

    public required DateTimeOffset CapturedAtUtc { get; init; }

    public string Status { get; init; } = "Idle";

    public string StatusDetail { get; init; } = string.Empty;
}
