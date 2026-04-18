namespace PoWatch.Shared.Models;

public sealed class StorageResetResultDto
{
    public bool Success { get; init; }
    public int ObservationsDeleted { get; init; }
    public int SubjectsDeleted { get; init; }
    public int BlobsDeleted { get; init; }
    public TimeSpan Duration { get; init; }
    public string? ErrorMessage { get; init; }
}
