using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Microsoft.Extensions.Options;
using PoWatch.Application.Contracts;
using PoWatch.Application.Models;
using PoWatch.Application.Options;

namespace PoWatch.Infrastructure.Persistence;

public sealed class AzureBlobSasProvider : IBlobSasProvider
{
    private readonly BlobContainerClient _containerClient;
    private readonly AzureStorageOptions _options;
    private readonly AzureStorageClients _clients;

    public AzureBlobSasProvider(AzureStorageClients clients, IOptions<AzureStorageOptions> options)
    {
        _clients = clients;
        _options = options.Value;
        _containerClient = clients.BlobService.GetBlobContainerClient(_options.SignificantImagesContainer);
    }

    public async Task<BlobAccessDescriptor> CreateUploadAccessAsync(string subjectId, DateOnly date, CancellationToken cancellationToken)
    {
        _clients.EnsureDevelopmentBlobCorsConfigured();
        await _containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var safeSubject = string.Join("-", subjectId.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        safeSubject = string.IsNullOrWhiteSpace(safeSubject) ? "unknown-subject" : safeSubject;

        var blobName = $"{date:yyyyMMdd}/{safeSubject}/{Guid.NewGuid():N}.svg";
        var blobClient = _containerClient.GetBlobClient(blobName);
        var expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(30);

        var sasUrl = blobClient.CanGenerateSasUri
            ? blobClient.GenerateSasUri(BlobSasPermissions.Create | BlobSasPermissions.Write, expiresAtUtc).ToString()
            : blobClient.Uri.ToString();

        return new BlobAccessDescriptor
        {
            SasUrl = sasUrl,
            BlobPath = $"{_options.SignificantImagesContainer}/{blobName}",
            ExpiresAtUtc = expiresAtUtc
        };
    }

    public async Task<string> CreateReadAccessUrlAsync(string blobPath, CancellationToken cancellationToken)
    {
        _clients.EnsureDevelopmentBlobCorsConfigured();
        await _containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var normalized = blobPath.Replace('\\', '/').TrimStart('/');
        if (normalized.StartsWith(_options.SignificantImagesContainer + "/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[(_options.SignificantImagesContainer.Length + 1)..];
        }

        var blobClient = _containerClient.GetBlobClient(normalized);
        var expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(30);

        var url = blobClient.CanGenerateSasUri
            ? blobClient.GenerateSasUri(BlobSasPermissions.Read, expiresAtUtc).ToString()
            : blobClient.Uri.ToString();

        return url;
    }
}
