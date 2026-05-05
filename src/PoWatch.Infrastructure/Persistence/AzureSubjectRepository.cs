using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PoWatch.Application.Contracts;
using PoWatch.Application.Options;
using PoWatch.Domain.Models;
using PoWatch.Domain.Services;

namespace PoWatch.Infrastructure.Persistence;

public sealed class AzureSubjectRepository : ISubjectRepository
{
    private readonly TableClient _tableClient;
    private readonly ILogger<AzureSubjectRepository> _logger;

    public AzureSubjectRepository(AzureStorageClients clients, IOptions<AzureStorageOptions> options, ILogger<AzureSubjectRepository> logger)
    {
        _tableClient = clients.TableService.GetTableClient(options.Value.SubjectsTable);
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubjectProfile>> GetAllAsync(CancellationToken cancellationToken)
    {
        try
        {
            var items = new List<SubjectProfile>();
            await foreach (var entity in _tableClient.QueryAsync<TableEntity>(cancellationToken: cancellationToken))
            {
                items.Add(Map(entity));
            }

            return items.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex,
                "Subject storage read failed — check Azurite/Azure connection. AzureStatus={Status} AzureErrorCode={ErrorCode} Detail={Detail}",
                ex.Status, ex.ErrorCode, ex.Message);
            throw;
        }
    }

    public async Task<SubjectProfile> GetOrCreateAsync(string? hint, CancellationToken cancellationToken)
    {
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
            SubjectIdSlugger.RegisterSlug(created.SubjectId, created.SubjectId);
            return created;
        }

        return await CreateAutoNumberedSubjectAsync(cancellationToken);
    }

    public async Task<SubjectProfile?> GetByIdAsync(string subjectId, CancellationToken cancellationToken)
    {
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
        var existing = await GetByIdAsync(subjectId, cancellationToken)
            ?? throw new InvalidOperationException($"Subject '{subjectId}' was not found.");

        var trimmed = newDisplayName.Trim();
        var canonicalId = await ResolveCanonicalSubjectIdAsync(subjectId, trimmed, cancellationToken, subjectId);

        var renamed = new SubjectProfile
        {
            SubjectId = canonicalId,
            DisplayName = trimmed,
            IsKnownIdentity = true,
            FirstSeenUtc = existing.FirstSeenUtc,
            LastSeenUtc = existing.LastSeenUtc
        };

        await UpsertAsync(renamed, cancellationToken);
        SubjectIdSlugger.UnregisterSlug(subjectId, subjectId);
        SubjectIdSlugger.RegisterSlug(renamed.SubjectId, renamed.SubjectId);

        if (!string.Equals(subjectId, canonicalId, StringComparison.OrdinalIgnoreCase))
        {
            await TryDeleteAsync(subjectId, cancellationToken);
        }

        return renamed;
    }

    public async Task<SubjectProfile> MergeAsync(string primarySubjectId, string secondarySubjectId, string? explicitName, CancellationToken cancellationToken)
    {
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
        var canonicalId = await ResolveCanonicalSubjectIdAsync(primary.SubjectId, displayName, cancellationToken, primarySubjectId, secondarySubjectId);

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
        SubjectIdSlugger.UnregisterSlug(primarySubjectId, primarySubjectId);
        SubjectIdSlugger.UnregisterSlug(secondarySubjectId, secondarySubjectId);
        SubjectIdSlugger.RegisterSlug(merged.SubjectId, merged.SubjectId);

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

    private async Task<SubjectProfile> CreateAutoNumberedSubjectAsync(CancellationToken cancellationToken)
    {
        var all = await GetAllAsync(cancellationToken);
        var nextNumber = all
            .Select(x => x.SubjectId)
            .Where(x => x.StartsWith("Subject-", StringComparison.OrdinalIgnoreCase))
            .Select(x => int.TryParse(x[8..], out var parsed) ? parsed : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;

        // Jitter reduces the probability that concurrent ingest requests start at the same
        // base number and race on the same Subject-N slot. Retry limit raised to 50.
        var jitter = Random.Shared.Next(0, 20);

        for (var attempt = 0; attempt < 50; attempt++)
        {
            var subjectId = $"Subject-{nextNumber + jitter + attempt}";
            var now = DateTimeOffset.UtcNow;
            var entity = new TableEntity("Subjects", subjectId)
            {
                ["DisplayName"] = subjectId,
                ["IsKnownIdentity"] = false,
                ["FirstSeenUtc"] = now,
                ["LastSeenUtc"] = now
            };

            try
            {
                await _tableClient.AddEntityAsync(entity, cancellationToken);
                _logger.LogInformation(
                    "Created auto-numbered subject. SubjectId={SubjectId} Attempt={Attempt}",
                    subjectId,
                    attempt + 1);
                SubjectIdSlugger.RegisterSlug(subjectId, subjectId);
                return Map(entity);
            }
            catch (RequestFailedException ex) when (ex.Status == 409)
            {
                _logger.LogWarning(
                    "Auto-numbered subject collision detected. Retrying with next identifier. SubjectId={SubjectId} Attempt={Attempt}",
                    subjectId,
                    attempt + 1);
            }
        }

        throw new InvalidOperationException("Unable to allocate a unique subject identifier after multiple retries.");
    }

    public async Task<SubjectProfile> RegisterKnownAsync(string displayName, CancellationToken cancellationToken)
    {
        var trimmed = displayName.Trim();
        var subjectId = await ResolveCanonicalSubjectIdAsync(string.Empty, trimmed, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        // If a subject with this canonical ID already exists, return it unchanged.
        var existing = await GetByIdAsync(subjectId, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "RegisterKnownAsync: subject already exists. SubjectId={SubjectId}",
                subjectId);
            return existing;
        }

        var profile = new SubjectProfile
        {
            SubjectId = subjectId,
            DisplayName = trimmed,
            IsKnownIdentity = true,
            FirstSeenUtc = now,
            LastSeenUtc = now
        };

        await UpsertAsync(profile, cancellationToken);
        SubjectIdSlugger.RegisterSlug(profile.SubjectId, profile.SubjectId);
        _logger.LogInformation(
            "RegisterKnownAsync: new known subject created. SubjectId={SubjectId} DisplayName={DisplayName}",
            subjectId,
            trimmed);

        return profile;
    }

    private async Task<string> ResolveCanonicalSubjectIdAsync(
        string currentSubjectId,
        string displayName,
        CancellationToken cancellationToken,
        params string[] allowedSubjectIds)
    {
        if (!string.IsNullOrWhiteSpace(currentSubjectId)
            && !currentSubjectId.StartsWith("Subject-", StringComparison.OrdinalIgnoreCase))
        {
            return currentSubjectId;
        }

        var baseId = SubjectIdSlugger.BuildCanonicalSubjectId(displayName);
        var candidate = baseId;
        var suffix = 2;

        while (true)
        {
            if (allowedSubjectIds.Any(id => string.Equals(id, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }

            var existing = await GetByIdAsync(candidate, cancellationToken);
            if (existing is null)
            {
                return candidate;
            }

            candidate = $"{baseId}-{suffix}";
            suffix++;
        }
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

    public async Task UpdateLastActivityAsync(string subjectId, string activity, bool isOutlier, CancellationToken cancellationToken)
    {
        try
        {
            var entity = new TableEntity("Subjects", subjectId)
            {
                ["LastActivity"] = activity,
                ["LastActivityIsOutlier"] = isOutlier
            };
            await _tableClient.UpdateEntityAsync(entity, ETag.All, TableUpdateMode.Merge, cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Subject row not yet created — ignore activity update.
        }
    }

    private static SubjectProfile Map(TableEntity entity) => new()
    {
        SubjectId = entity.RowKey,
        DisplayName = entity.GetString("DisplayName") ?? entity.RowKey,
        IsKnownIdentity = entity.GetBoolean("IsKnownIdentity") ?? false,
        FirstSeenUtc = entity.GetDateTimeOffset("FirstSeenUtc") ?? DateTimeOffset.UtcNow,
        LastSeenUtc = entity.GetDateTimeOffset("LastSeenUtc") ?? DateTimeOffset.UtcNow,
        LastActivity = entity.GetString("LastActivity"),
        LastActivityIsOutlier = entity.GetBoolean("LastActivityIsOutlier") ?? false
    };
}
