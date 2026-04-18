namespace PoWatch.Application.Options;

public sealed class AzureStorageOptions
{
    public string ConnectionString { get; init; } = string.Empty;
    public string ServiceUri { get; init; } = string.Empty;
    public string ObservationsTable { get; init; } = "PoWatchObservations";
    public string SubjectsTable { get; init; } = "PoWatchSubjects";
    public string SignificantImagesContainer { get; init; } = "significant-images";
    public string[] DevCorsAllowedOrigins { get; init; } = [];

    /// <summary>
    /// When true, skips Azure Storage table/container initialization at startup.
    /// Set this in Development when Azurite / Docker is unavailable.
    /// </summary>
    public bool SkipStorageInit { get; init; } = false;
}
