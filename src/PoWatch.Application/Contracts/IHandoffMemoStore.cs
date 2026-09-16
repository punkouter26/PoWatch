namespace PoWatch.Application.Contracts;

/// <summary>Blob-storage abstraction for handoff voice memo audio. Separate from
/// <see cref="IHandoffMemoRepository"/> so the row store and the byte store can pick their own
/// backend. Upload returns the storage path; the bytes are read back via
/// <see cref="DownloadAsync"/> for the proxy endpoint.</summary>
public interface IHandoffMemoStore
{
    /// <summary>Upload audio bytes; returns the blob path inside the memos container.</summary>
    Task<string> UploadAsync(string memoId, string contentType, byte[] bytes, CancellationToken cancellationToken);

    /// <summary>Download audio bytes. Returns null when the blob has been purged or was never
    /// written (which would indicate a metadata-only row leaked through).</summary>
    Task<(string ContentType, byte[] Bytes)?> DownloadAsync(string blobPath, CancellationToken cancellationToken);

    Task DeleteAsync(string blobPath, CancellationToken cancellationToken);
}
