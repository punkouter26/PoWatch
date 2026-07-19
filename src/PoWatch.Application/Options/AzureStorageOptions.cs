using System.ComponentModel.DataAnnotations;

namespace PoWatch.Application.Options;

public sealed class AzureStorageOptions
{
    public string ConnectionString { get; init; } = string.Empty;
    public string ServiceUri { get; init; } = string.Empty;

    // Azure table/container names must be non-empty and at least 3 chars. These carry defaults, so
    // validation only trips when an operator overrides them to something invalid — fail-fast at boot.
    [Required, MinLength(3)]
    public string ObservationsTable { get; init; } = "PoWatchObservations";

    [Required, MinLength(3)]
    public string SubjectsTable { get; init; } = "PoWatchSubjects";

    [Required, MinLength(3)]
    public string SignificantImagesContainer { get; init; } = "significant-images";

    // Blob container holding the persisted Data Protection keyring (BFF cookie encryption keys).
    [Required, MinLength(3)]
    public string DataProtectionKeysContainer { get; init; } = "dataprotection-keys";

    public string[] DevCorsAllowedOrigins { get; init; } = [];

    /// <summary>
    /// When true, skips Azure Storage table/container initialization at startup.
    /// Set this in Development when Azurite / Docker is unavailable.
    /// </summary>
    public bool SkipStorageInit { get; init; } = false;
}
