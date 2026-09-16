using System.Collections.Concurrent;
using PoWatch.Application.Contracts;

namespace PoWatch.Infrastructure.Persistence;

/// <summary>Thread-safe in-memory implementation of <see cref="IHandoffMemoStore"/>. Stores bytes
/// keyed by blob path; the production Azure variant lives next to AzureSubjectRepository once
/// the blob-init pipeline is ready for it.</summary>
public sealed class InMemoryHandoffMemoStore : IHandoffMemoStore
{
    private readonly ConcurrentDictionary<string, (string ContentType, byte[] Bytes)> _blobs = new(StringComparer.Ordinal);

    public Task<string> UploadAsync(string memoId, string contentType, byte[] bytes, CancellationToken cancellationToken)
    {
        var path = $"handoff-memos/{memoId}.bin";
        _blobs[path] = (contentType, bytes);
        return Task.FromResult(path);
    }

    public Task<(string ContentType, byte[] Bytes)?> DownloadAsync(string blobPath, CancellationToken cancellationToken)
    {
        if (_blobs.TryGetValue(blobPath, out var entry))
        {
            return Task.FromResult<(string, byte[])?>(entry);
        }
        return Task.FromResult<(string, byte[])?>(null);
    }

    public Task DeleteAsync(string blobPath, CancellationToken cancellationToken)
    {
        _blobs.TryRemove(blobPath, out _);
        return Task.CompletedTask;
    }
}
