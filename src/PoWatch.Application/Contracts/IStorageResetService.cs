using PoWatch.Shared.Models;

namespace PoWatch.Application.Contracts;

public interface IStorageResetService
{
    Task<StorageResetResultDto> ResetAllAsync(CancellationToken cancellationToken);
}
