using PoWatch.Domain.Models;

namespace PoWatch.Application.Contracts;

/// <summary>Append + lookup repository for family share links. Production Azure-backed variant
/// belongs alongside AzureSubjectRepository once the rest of the storage pipeline is ready.</summary>
public interface IShareLinkRepository
{
    Task<ShareLink> AddAsync(ShareLink link, CancellationToken cancellationToken);

    Task<ShareLink?> GetAsync(string id, CancellationToken cancellationToken);

    Task UpdateAsync(ShareLink link, CancellationToken cancellationToken);
}
