using System.Collections.Concurrent;
using PoWatch.Application.Contracts;
using PoWatch.Domain.Models;

namespace PoWatch.Infrastructure.Persistence;

/// <summary>Thread-safe in-memory implementation of <see cref="IShareLinkRepository"/>. Production
/// Azure-backed variant belongs alongside AzureSubjectRepository once the storage pipeline is ready.</summary>
public sealed class InMemoryShareLinkRepository : IShareLinkRepository
{
    private readonly ConcurrentDictionary<string, ShareLink> _byId = new(StringComparer.Ordinal);

    public Task<ShareLink> AddAsync(ShareLink link, CancellationToken cancellationToken)
    {
        if (!_byId.TryAdd(link.Id, link))
        {
            throw new InvalidOperationException($"Share link with id '{link.Id}' already exists.");
        }
        return Task.FromResult(link);
    }

    public Task<ShareLink?> GetAsync(string id, CancellationToken cancellationToken)
    {
        _byId.TryGetValue(id, out var link);
        return Task.FromResult(link);
    }

    public Task UpdateAsync(ShareLink link, CancellationToken cancellationToken)
    {
        // ShareLink.RevokedAtUtc is the only mutable property; everything else is part of the
        // creation-time snapshot. The lookup-or-add pattern keeps the operation atomic against
        // concurrent revocations.
        _byId[link.Id] = link;
        return Task.CompletedTask;
    }
}
