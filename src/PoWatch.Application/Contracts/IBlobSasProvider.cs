using PoWatch.Shared.Models;

namespace PoWatch.Application.Contracts;

public interface IBlobSasProvider
{
    Task<BlobAccessDescriptorDto> CreateUploadAccessAsync(string subjectId, DateOnly date, CancellationToken cancellationToken);

    Task<BlobAccessDescriptorDto> CreateUploadAccessForBlobAsync(string blobPath, CancellationToken cancellationToken);

    Task<string> CreateReadAccessUrlAsync(string blobPath, CancellationToken cancellationToken);
}
