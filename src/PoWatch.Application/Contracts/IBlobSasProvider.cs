using PoWatch.Application.Models;

namespace PoWatch.Application.Contracts;

public interface IBlobSasProvider
{
    Task<BlobAccessDescriptor> CreateUploadAccessAsync(string subjectId, DateOnly date, CancellationToken cancellationToken);

    Task<string> CreateReadAccessUrlAsync(string blobPath, CancellationToken cancellationToken);
}
