namespace PoWatch.Shared.Models;

public sealed class DiagnosticsSnapshotDto
{
    public DateTimeOffset CapturedAtUtc { get; init; }
    public double CpuLoadPercent { get; init; }
    public double MemoryMb { get; init; }
    public string StorageConnectionStatus { get; init; } = string.Empty;
    public string MaskedEndpoint { get; init; } = string.Empty;
    public string MaskedApiKey { get; init; } = string.Empty;
}
