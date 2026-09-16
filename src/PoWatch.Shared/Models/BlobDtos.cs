namespace PoWatch.Shared.Models;

public sealed class BlobAccessDescriptorDto
{
    public string SasUrl { get; init; } = string.Empty;
    public string BlobPath { get; init; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; init; }
}
