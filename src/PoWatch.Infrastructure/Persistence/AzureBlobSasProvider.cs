using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PoWatch.Application.Contracts;
using PoWatch.Application.Options;
using PoWatch.Shared.Models;

namespace PoWatch.Infrastructure.Persistence;

public sealed class AzureBlobSasProvider : IBlobSasProvider
{
    private readonly BlobContainerClient _containerClient;
    private readonly AzureStorageOptions _options;
    private readonly AzureStorageClients _clients;
    private readonly ILogger<AzureBlobSasProvider> _logger;

    public AzureBlobSasProvider(AzureStorageClients clients, IOptions<AzureStorageOptions> options, ILogger<AzureBlobSasProvider>? logger = null)
    {
        _clients = clients;
        _options = options.Value;
        _containerClient = clients.BlobService.GetBlobContainerClient(_options.SignificantImagesContainer);
        _logger = logger ?? NullLogger<AzureBlobSasProvider>.Instance;
    }

    public async Task<BlobAccessDescriptorDto> CreateUploadAccessAsync(string subjectId, DateOnly date, CancellationToken cancellationToken)
    {
        var safeSubject = string.Join("-", subjectId.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        safeSubject = string.IsNullOrWhiteSpace(safeSubject) ? "unknown-subject" : safeSubject;

        var blobName = $"{date:yyyyMMdd}/{safeSubject}/{Guid.NewGuid():N}.svg";

        _logger.LogInformation("Generating upload SAS. SubjectId={SubjectId} Date={Date} BlobPath={BlobPath}", subjectId, date, $"{_options.SignificantImagesContainer}/{blobName}");

        return await CreateUploadAccessCoreAsync(blobName, cancellationToken);
    }

    public async Task<BlobAccessDescriptorDto> CreateUploadAccessForBlobAsync(string blobPath, CancellationToken cancellationToken)
    {
        var normalized = NormalizeBlobName(blobPath);

        _logger.LogInformation("Generating upload SAS for existing blob path. BlobPath={BlobPath}", $"{_options.SignificantImagesContainer}/{normalized}");

        return await CreateUploadAccessCoreAsync(normalized, cancellationToken);
    }

    public async Task<string> CreateReadAccessUrlAsync(string blobPath, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Generating read SAS. BlobPath={BlobPath}", blobPath);
        _clients.EnsureDevelopmentBlobCorsConfigured();

        var normalized = NormalizeBlobName(blobPath);
        var blobClient = _containerClient.GetBlobClient(normalized);
        var expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(30);

        // Ensure we can always generate a SAS token for secure read access
        // CanGenerateSasUri checks for StorageSharedKeyCredential or UserDelegationKey
        if (!blobClient.CanGenerateSasUri)
        {
            _logger.LogWarning(
                "Cannot generate SAS URI with current credential for blob {BlobPath}. Ensure Managed Identity has 'Storage Blob Data Reader' role and container is not public.",
                normalized);
            // As fallback, return unsigned URL for blobs that might be in public containers (not recommended for prod)
            // In production, all private blobs must use SAS tokens
        }

        var url = blobClient.CanGenerateSasUri
            ? blobClient.GenerateSasUri(BlobSasPermissions.Read, expiresAtUtc).ToString()
            : blobClient.Uri.ToString();

        _logger.LogInformation("Generated read URL for blob {BlobPath}. HasSasToken={HasSasToken}", normalized, url.Contains("?"));
        return url;
    }

    private async Task<BlobAccessDescriptorDto> CreateUploadAccessCoreAsync(string blobName, CancellationToken cancellationToken)
    {
        _clients.EnsureDevelopmentBlobCorsConfigured();

        var blobClient = _containerClient.GetBlobClient(blobName);
        var expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(30);

        var sasUrl = blobClient.CanGenerateSasUri
            ? blobClient.GenerateSasUri(BlobSasPermissions.Create | BlobSasPermissions.Write, expiresAtUtc).ToString()
            : blobClient.Uri.ToString();

        return new BlobAccessDescriptorDto
        {
            SasUrl = sasUrl,
            BlobPath = $"{_options.SignificantImagesContainer}/{blobName}",
            ExpiresAtUtc = expiresAtUtc
        };
    }

    private string NormalizeBlobName(string blobPath)
    {
        var normalized = blobPath.Replace('\\', '/').TrimStart('/');
        if (normalized.StartsWith(_options.SignificantImagesContainer + "/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[(_options.SignificantImagesContainer.Length + 1)..];
        }

        return normalized;
    }
}
