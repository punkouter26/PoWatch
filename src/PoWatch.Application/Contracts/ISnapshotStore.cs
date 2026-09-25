namespace PoWatch.Application.Contracts;

public sealed record SnapshotUpload(string Path, Uri UploadUrl);

/// <summary>
/// Highlight snapshots: the only images that ever leave the browser. Paths are always under the
/// caller's own prefix, and each user may keep at most <see cref="DailyCap"/> per local day.
/// </summary>
public interface ISnapshotStore
{
    public const int DailyCap = 200;

    /// <summary>A write-only link for one new snapshot, or null when today's cap is reached.</summary>
    Task<SnapshotUpload?> CreateUploadAsync(string userId, DateOnly localDay, CancellationToken cancellationToken);

    /// <summary>A short-lived read link, or null when the path is not one of the caller's snapshots.</summary>
    Task<Uri?> CreateReadUrlAsync(string userId, string path, CancellationToken cancellationToken);
}
