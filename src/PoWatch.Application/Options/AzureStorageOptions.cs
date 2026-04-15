namespace PoWatch.Application.Options;

public sealed class AzureStorageOptions
{
    public string ConnectionString { get; init; } = string.Empty;
    public string ObservationsTable { get; init; } = "PoWatchObservations";
    public string SubjectsTable { get; init; } = "PoWatchSubjects";
    public string SubjectIndexTable { get; init; } = "PoWatchSubjectIndex";
    public string SignificantImagesContainer { get; init; } = "significant-images";
}
