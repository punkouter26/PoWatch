using System.Text;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using PoWatch.Application.Contracts;
using PoWatch.Application.Options;
using PoWatch.Domain.Models;

namespace PoWatch.Infrastructure.Persistence;

public sealed class AzureSubjectRepository : ISubjectRepository
{
    private readonly TableClient _tableClient;

    public AzureSubjectRepository(AzureStorageClients clients, IOptions<AzureStorageOptions> options)
    {
        _tableClient = clients.TableService.GetTableClient(options.Value.SubjectsTable);
    }

    public async Task<IReadOnlyList<SubjectProfile>> GetAllAsync(CancellationToken cancellationToken)
    {
        await _tableClient.CreateIfNotExistsAsync(cancellationToken);

        var items = new List<SubjectProfile>();
        await foreach (var entity in _tableClient.QueryAsync<TableEntity>(cancellationToken: cancellationToken))
        {
            items.Add(Map(entity));
        }

        return items.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<SubjectProfile> GetOrCreateAsync(string? hint, CancellationToken cancellationToken)
    {
        await _tableClient.CreateIfNotExistsAsync(cancellationToken);

        var normalized = (hint ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            var existing = await GetByIdAsync(normalized, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            var now = DateTimeOffset.UtcNow;
            var created = new SubjectProfile
            {
                SubjectId = normalized,
                DisplayName = normalized,
                IsKnownIdentity = !normalized.StartsWith("Subject-", StringComparison.OrdinalIgnoreCase),
                FirstSeenUtc = now,
                LastSeenUtc = now
            };

            await UpsertAsync(created, cancellationToken);
            return created;
        }

        var all = await GetAllAsync(cancellationToken);
        var nextNumber = all
            .Select(x => x.SubjectId)
            .Where(x => x.StartsWith("Subject-", StringComparison.OrdinalIgnoreCase))
            .Select(x => int.TryParse(x[8..], out var parsed) ? parsed : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;

        var subjectId = $"Subject-{nextNumber}";
        var profile = new SubjectProfile
        {
            SubjectId = subjectId,
            DisplayName = subjectId,
            IsKnownIdentity = false,
            FirstSeenUtc = DateTimeOffset.UtcNow,
            LastSeenUtc = DateTimeOffset.UtcNow
        };

        await UpsertAsync(profile, cancellationToken);
        return profile;
    }

    public async Task<SubjectProfile?> GetByIdAsync(string subjectId, CancellationToken cancellationToken)
    {
        await _tableClient.CreateIfNotExistsAsync(cancellationToken);

        try
        {
            var entity = await _tableClient.GetEntityAsync<TableEntity>("Subjects", subjectId, cancellationToken: cancellationToken);
            return Map(entity.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<SubjectProfile> RenameAsync(string subjectId, string newDisplayName, CancellationToken cancellationToken)
    {
        await _tableClient.CreateIfNotExistsAsync(cancellationToken);

        var existing = await GetByIdAsync(subjectId, cancellationToken)
            ?? throw new InvalidOperationException($"Subject '{subjectId}' was not found.");

        var trimmed = newDisplayName.Trim();
        var canonicalId = ResolveCanonicalSubjectId(subjectId, trimmed);

        var renamed = new SubjectProfile
        {
            SubjectId = canonicalId,
            DisplayName = trimmed,
            IsKnownIdentity = true,
            FirstSeenUtc = existing.FirstSeenUtc,
            LastSeenUtc = existing.LastSeenUtc
        };

        await UpsertAsync(renamed, cancellationToken);

        if (!string.Equals(subjectId, canonicalId, StringComparison.OrdinalIgnoreCase))
        {
            await TryDeleteAsync(subjectId, cancellationToken);
        }

        return renamed;
    }

    public async Task<SubjectProfile> MergeAsync(string primarySubjectId, string secondarySubjectId, string? explicitName, CancellationToken cancellationToken)
    {
        await _tableClient.CreateIfNotExistsAsync(cancellationToken);

        var primary = await GetByIdAsync(primarySubjectId, cancellationToken) ?? new SubjectProfile
        {
            SubjectId = primarySubjectId,
            DisplayName = primarySubjectId,
            IsKnownIdentity = !primarySubjectId.StartsWith("Subject-", StringComparison.OrdinalIgnoreCase),
            FirstSeenUtc = DateTimeOffset.UtcNow,
            LastSeenUtc = DateTimeOffset.UtcNow
        };

        var secondary = await GetByIdAsync(secondarySubjectId, cancellationToken);
        var displayName = string.IsNullOrWhiteSpace(explicitName) ? primary.DisplayName : explicitName.Trim();
        var canonicalId = ResolveCanonicalSubjectId(primary.SubjectId, displayName);

        var merged = new SubjectProfile
        {
            SubjectId = canonicalId,
            DisplayName = displayName,
            IsKnownIdentity = true,
            FirstSeenUtc = secondary is null
                ? primary.FirstSeenUtc
                : new[] { primary.FirstSeenUtc, secondary.FirstSeenUtc }.Min(),
            LastSeenUtc = secondary is null
                ? primary.LastSeenUtc
                : new[] { primary.LastSeenUtc, secondary.LastSeenUtc }.Max()
        };

        await UpsertAsync(merged, cancellationToken);

        if (!string.Equals(primarySubjectId, merged.SubjectId, StringComparison.OrdinalIgnoreCase))
        {
            await TryDeleteAsync(primarySubjectId, cancellationToken);
        }

        await TryDeleteAsync(secondarySubjectId, cancellationToken);

        return merged;
    }

    private async Task UpsertAsync(SubjectProfile profile, CancellationToken cancellationToken)
    {
        var entity = new TableEntity("Subjects", profile.SubjectId)
        {
            ["DisplayName"] = profile.DisplayName,
            ["IsKnownIdentity"] = profile.IsKnownIdentity,
            ["FirstSeenUtc"] = profile.FirstSeenUtc,
            ["LastSeenUtc"] = profile.LastSeenUtc
        };

        await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
    }

    private async Task TryDeleteAsync(string subjectId, CancellationToken cancellationToken)
    {
        try
        {
            await _tableClient.DeleteEntityAsync("Subjects", subjectId, cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Already removed — safe to ignore.
        }
    }

    private static string ResolveCanonicalSubjectId(string currentSubjectId, string displayName)
    {
        if (!currentSubjectId.StartsWith("Subject-", StringComparison.OrdinalIgnoreCase)
            || string.Equals(currentSubjectId, displayName, StringComparison.OrdinalIgnoreCase))
        {
            return currentSubjectId;
        }

        return BuildCanonicalSubjectId(displayName);
    }

    private static string BuildCanonicalSubjectId(string displayName)
    {
        var builder = new StringBuilder();

        foreach (var character in displayName.Trim().ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '-');
        }

        return builder.ToString().Trim('-');
    }

    private static SubjectProfile Map(TableEntity entity) => new()
    {
        SubjectId = entity.RowKey,
        DisplayName = entity.GetString("DisplayName") ?? entity.RowKey,
        IsKnownIdentity = entity.GetBoolean("IsKnownIdentity") ?? false,
        FirstSeenUtc = entity.GetDateTimeOffset("FirstSeenUtc") ?? DateTimeOffset.UtcNow,
        LastSeenUtc = entity.GetDateTimeOffset("LastSeenUtc") ?? DateTimeOffset.UtcNow
    };
}
