namespace PoWatch.Application.Models;

public sealed class BlobAccessDescriptor
{
    public string SasUrl { get; init; } = string.Empty;

    public string BlobPath { get; init; } = string.Empty;

    public DateTimeOffset ExpiresAtUtc { get; init; }
}
