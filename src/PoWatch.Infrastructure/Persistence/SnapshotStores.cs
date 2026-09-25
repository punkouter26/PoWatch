using System.Globalization;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Microsoft.Extensions.Options;
using PoWatch.Application.Contracts;
using PoWatch.Application.Options;

namespace PoWatch.Infrastructure.Persistence;

/// <summary>Snapshots in Blob Storage at {user}/{yyyyMMdd}/{id}.jpg, handed out as short-lived SAS links.</summary>
public sealed class AzureSnapshotStore(AzureStorageClients clients, IOptions<AzureStorageOptions> options) : ISnapshotStore
{
    private static readonly TimeSpan UploadWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ReadWindow = TimeSpan.FromMinutes(30);

    private readonly BlobContainerClient _container = clients.BlobService.GetBlobContainerClient(options.Value.SnapshotsContainer);

    public bool IsAvailable => true;

    public async Task<SnapshotUpload?> CreateUploadAsync(string userId, DateOnly localDay, CancellationToken cancellationToken)
    {
        clients.EnsureDevelopmentBlobCorsConfigured();
        var prefix = $"{Prefix(userId)}{localDay.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}/";

        var count = 0;
        await foreach (var _ in _container.GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken))
        {
            if (++count >= ISnapshotStore.DailyCap) return null;
        }

        var path = $"{prefix}{Guid.NewGuid():N}.jpg";
        var blob = _container.GetBlobClient(path);
        var url = blob.GenerateSasUri(BlobSasPermissions.Create | BlobSasPermissions.Write, DateTimeOffset.UtcNow.Add(UploadWindow));
        return new SnapshotUpload(path, url);
    }

    public Task<Uri?> CreateReadUrlAsync(string userId, string path, CancellationToken cancellationToken)
    {
        if (!IsOwnedBy(userId, path)) return Task.FromResult<Uri?>(null);
        clients.EnsureDevelopmentBlobCorsConfigured();
        var url = _container.GetBlobClient(path).GenerateSasUri(BlobSasPermissions.Read, DateTimeOffset.UtcNow.Add(ReadWindow));
        return Task.FromResult<Uri?>(url);
    }

    internal static string Prefix(string userId) => $"{StatsEntityMapper.UserKey(userId)}/";

    internal static bool IsOwnedBy(string userId, string path) =>
        !string.IsNullOrWhiteSpace(path)
        && path.StartsWith(Prefix(userId), StringComparison.Ordinal)
        && !path.Contains("..", StringComparison.Ordinal)
        && path.EndsWith(".jpg", StringComparison.Ordinal);
}

/// <summary>No storage configured: moments are kept without pictures.</summary>
public sealed class InMemorySnapshotStore : ISnapshotStore
{
    public bool IsAvailable => false;

    public Task<SnapshotUpload?> CreateUploadAsync(string userId, DateOnly localDay, CancellationToken cancellationToken) =>
        Task.FromResult<SnapshotUpload?>(null);

    public Task<Uri?> CreateReadUrlAsync(string userId, string path, CancellationToken cancellationToken) =>
        Task.FromResult<Uri?>(null);
}
