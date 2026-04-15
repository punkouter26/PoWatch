using System.Diagnostics;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PoWatch.Application.Contracts;
using PoWatch.Application.Options;
using PoWatch.Domain.Models;

namespace PoWatch.Infrastructure.Persistence;

public sealed class AzureObservationRepository : IObservationRepository
{
    private static readonly ActivitySource ActivitySource = new("PoWatch.Storage");
    private readonly TableClient _tableClient;
    private readonly ILogger<AzureObservationRepository> _logger;

    public AzureObservationRepository(AzureStorageClients clients, IOptions<AzureStorageOptions> options, ILogger<AzureObservationRepository> logger)
    {
        _tableClient = clients.TableService.GetTableClient(options.Value.ObservationsTable);
        _logger = logger;
    }

    public async Task AddAsync(ObservationEvent observation, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("Storage.AddObservation");

        await _tableClient.CreateIfNotExistsAsync(cancellationToken);

        var partitionKey = DateOnly.FromDateTime(observation.ObservedAtUtc.UtcDateTime).ToString("yyyyMMdd");
        var invertedTicks = DateTime.MaxValue.Ticks - observation.ObservedAtUtc.UtcDateTime.Ticks;
        var rowKey = $"{observation.SubjectId}_{invertedTicks:D19}_{observation.Id:N}";

        activity?.SetTag("powatch.subject_id", observation.SubjectId);
        activity?.SetTag("powatch.partition_key", partitionKey);
        activity?.SetTag("powatch.is_significant", observation.IsSignificant);

        var entity = new TableEntity(partitionKey, rowKey)
        {
            ["Id"] = observation.Id.ToString("N"),
            ["ObservedAtUtc"] = observation.ObservedAtUtc,
            ["SubjectId"] = observation.SubjectId,
            ["SubjectDisplayName"] = observation.SubjectDisplayName,
            ["Activity"] = observation.Activity,
            ["ClinicalDescription"] = observation.ClinicalDescription,
            ["IsSignificant"] = observation.IsSignificant,
            ["SignificantReason"] = observation.SignificantReason,
            ["IsClinicalOutlier"] = observation.IsClinicalOutlier,
            ["ImageReference"] = observation.ImageReference
        };

        await _tableClient.AddEntityAsync(entity, cancellationToken);

        _logger.LogInformation(
            "Observation storage write completed. SubjectId={SubjectId} PartitionKey={PartitionKey} TraceId={TraceId}",
            observation.SubjectId,
            partitionKey,
            Activity.Current?.TraceId.ToString());
    }

    public async Task<IReadOnlyList<ObservationEvent>> GetByDateAsync(DateOnly date, CancellationToken cancellationToken)
    {
        await _tableClient.CreateIfNotExistsAsync(cancellationToken);

        var partitionKey = date.ToString("yyyyMMdd");
        var items = new List<ObservationEvent>();

        await foreach (var entity in _tableClient.QueryAsync<TableEntity>(
                           filter: $"PartitionKey eq '{partitionKey}'",
                           cancellationToken: cancellationToken))
        {
            items.Add(Map(entity));
        }

        var ordered = items.OrderBy(x => x.ObservedAtUtc).ToList();

        _logger.LogDebug(
            "Observation chapter read completed. Date={Date} Count={Count} TraceId={TraceId}",
            date,
            ordered.Count,
            Activity.Current?.TraceId.ToString());

        return ordered;
    }

    public async Task<ObservationEvent?> GetLatestForSubjectAsync(string subjectId, CancellationToken cancellationToken)
    {
        await _tableClient.CreateIfNotExistsAsync(cancellationToken);

        var safeSubjectId = subjectId.Replace("'", "''");
        var items = new List<ObservationEvent>();

        await foreach (var entity in _tableClient.QueryAsync<TableEntity>(
                           filter: $"SubjectId eq '{safeSubjectId}'",
                           cancellationToken: cancellationToken))
        {
            items.Add(Map(entity));
        }

        return items.OrderByDescending(x => x.ObservedAtUtc).FirstOrDefault();
    }

    public async Task<int> MergeSubjectAsync(string oldSubjectId, SubjectProfile target, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("Storage.RewriteSubjectHistory");

        await _tableClient.CreateIfNotExistsAsync(cancellationToken);

        var safeSubjectId = oldSubjectId.Replace("'", "''");
        var entities = new List<TableEntity>();

        await foreach (var entity in _tableClient.QueryAsync<TableEntity>(
                           filter: $"SubjectId eq '{safeSubjectId}'",
                           cancellationToken: cancellationToken))
        {
            entities.Add(entity);
        }

        foreach (var entity in entities)
        {
            var updated = RewriteEntity(entity, target);

            await _tableClient.UpsertEntityAsync(updated, TableUpdateMode.Replace, cancellationToken);

            if (!string.Equals(entity.RowKey, updated.RowKey, StringComparison.OrdinalIgnoreCase))
            {
                await _tableClient.DeleteEntityAsync(entity.PartitionKey, entity.RowKey, entity.ETag, cancellationToken: cancellationToken);
            }
        }

        _logger.LogInformation(
            "Subject history rewrite completed. OldSubjectId={OldSubjectId} CanonicalSubjectId={CanonicalSubjectId} EventsRewritten={EventsRewritten} TraceId={TraceId}",
            oldSubjectId,
            target.SubjectId,
            entities.Count,
            Activity.Current?.TraceId.ToString());

        return entities.Count;
    }

    private static TableEntity RewriteEntity(TableEntity source, SubjectProfile target)
    {
        var id = source.GetString("Id") ?? Guid.NewGuid().ToString("N");
        var observedAt = source.GetDateTimeOffset("ObservedAtUtc") ?? DateTimeOffset.UtcNow;

        return new TableEntity(source.PartitionKey, BuildRowKey(target.SubjectId, observedAt.UtcDateTime, Guid.Parse(id)))
        {
            ["Id"] = id,
            ["ObservedAtUtc"] = observedAt,
            ["SubjectId"] = target.SubjectId,
            ["SubjectDisplayName"] = target.DisplayName,
            ["Activity"] = source.GetString("Activity") ?? string.Empty,
            ["ClinicalDescription"] = source.GetString("ClinicalDescription") ?? string.Empty,
            ["IsSignificant"] = source.GetBoolean("IsSignificant") ?? false,
            ["SignificantReason"] = source.GetString("SignificantReason"),
            ["IsClinicalOutlier"] = source.GetBoolean("IsClinicalOutlier") ?? false,
            ["ImageReference"] = source.GetString("ImageReference")
        };
    }

    private static string BuildRowKey(string subjectId, DateTime observedAtUtc, Guid id)
    {
        var invertedTicks = DateTime.MaxValue.Ticks - observedAtUtc.Ticks;
        return $"{subjectId}_{invertedTicks:D19}_{id:N}";
    }

    private static ObservationEvent Map(TableEntity entity) => new()
    {
        Id = Guid.Parse(entity.GetString("Id") ?? Guid.NewGuid().ToString("N")),
        ObservedAtUtc = entity.GetDateTimeOffset("ObservedAtUtc") ?? DateTimeOffset.UtcNow,
        SubjectId = entity.GetString("SubjectId") ?? string.Empty,
        SubjectDisplayName = entity.GetString("SubjectDisplayName") ?? string.Empty,
        Activity = entity.GetString("Activity") ?? string.Empty,
        ClinicalDescription = entity.GetString("ClinicalDescription") ?? string.Empty,
        IsSignificant = entity.GetBoolean("IsSignificant") ?? false,
        SignificantReason = entity.GetString("SignificantReason"),
        IsClinicalOutlier = entity.GetBoolean("IsClinicalOutlier") ?? false,
        ImageReference = entity.GetString("ImageReference")
    };
}
