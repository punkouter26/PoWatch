using PoWatch.Domain.Models;

namespace PoWatch.Application.Contracts;

/// <summary>A user's regulars — recurring people, pets, cars and objects.</summary>
public interface IRegularStore
{
    Task<IReadOnlyList<Regular>> ListAsync(string userId, CancellationToken cancellationToken);

    Task<Regular?> GetAsync(string userId, string regularId, CancellationToken cancellationToken);

    Task UpsertAsync(Regular regular, CancellationToken cancellationToken);

    Task DeleteAsync(string userId, string regularId, CancellationToken cancellationToken);
}
