using Microsoft.Extensions.Logging;
using PoWatch.Application.Contracts;
using PoWatch.Application.Models;

namespace PoWatch.Application.Services;

public sealed class BlobSasService(IBlobSasProvider provider, ILogger<BlobSasService> logger)
{
    public async Task<BlobAccessDescriptor> CreateUploadAccessAsync(string subjectId, DateOnly date, CancellationToken cancellationToken)
    {
        var sanitizedSubject = string.IsNullOrWhiteSpace(subjectId) ? "unknown-subject" : subjectId.Trim();

        logger.LogInformation("Generating upload SAS. SubjectId={SubjectId} Date={Date}", sanitizedSubject, date);
        return await provider.CreateUploadAccessAsync(sanitizedSubject, date, cancellationToken);
    }

    public async Task<string> CreateReadAccessUrlAsync(string blobPath, CancellationToken cancellationToken)
    {
        logger.LogDebug("Generating read SAS. BlobPath={BlobPath}", blobPath);
        return await provider.CreateReadAccessUrlAsync(blobPath, cancellationToken);
    }
}
