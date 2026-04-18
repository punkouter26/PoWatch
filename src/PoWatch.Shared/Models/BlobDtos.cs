namespace PoWatch.Shared.Models;

public sealed class BlobAccessDescriptorDto
{
    public string SasUrl { get; init; } = string.Empty;
    public string BlobPath { get; init; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; init; }
}

/// <summary>
/// Evidence blob integrity check result — verifies that a blob can be read with proper SAS authorization.
/// Used by QA/diagnostics to validate that uploaded images are viewable in the UI.
/// </summary>
public sealed class BlobIntegrityCheckDto
{
    /// <summary>
    /// Full blob path (e.g., "significant-images/20260418/Subject-15/guid.svg").
    /// </summary>
    public string BlobPath { get; init; } = string.Empty;

    /// <summary>
    /// Status: "HasValidSas", "NoSasToken", "Error", etc.
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// The signed read URL if generation succeeded.
    /// </summary>
    public string? ReadUrl { get; init; }

    /// <summary>
    /// When the SAS token expires (if applicable).
    /// </summary>
    public DateTimeOffset? ExpiresAtUtc { get; init; }

    /// <summary>
    /// Whether the blob can be viewed by following ReadUrl.
    /// </summary>
    public bool IsViewable { get; init; }

    /// <summary>
    /// Error message if status == "Error".
    /// </summary>
    public string? ErrorMessage { get; init; }
}
