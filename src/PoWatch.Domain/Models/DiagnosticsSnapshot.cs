namespace PoWatch.Domain.Models;

public sealed class DiagnosticsSnapshot
{
    public required DateTimeOffset CapturedAtUtc { get; init; }

    public required double CpuLoadPercent { get; init; }

    public required double MemoryMb { get; init; }

    public required string StorageConnectionStatus { get; init; }

    public required string MaskedEndpoint { get; init; }

    public required string MaskedApiKey { get; init; }
}
