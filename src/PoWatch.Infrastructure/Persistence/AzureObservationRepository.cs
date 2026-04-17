using System.Diagnostics;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using PoWatch.Application.Contracts;
using PoWatch.Application.Options;
using PoWatch.Domain.Models;

namespace PoWatch.Infrastructure.Persistence;

public sealed class AzureObservationRepository : IObservationRepository
{
    private static readonly ActivitySource ActivitySource = new("PoWatch.Storage");
    private readonly TableClient _tableClient;
    private readonly ILogger<AzureObservationRepository> _logger;
    private readonly ResiliencePipeline _retryPipeline;

    public AzureObservationRepository(AzureStorageClients clients, IOptions<AzureStorageOptions> options, ILogger<AzureObservationRepository> logger)
    {
        _tableClient = clients.TableService.GetTableClient(options.Value.ObservationsTable);
        _logger = logger;

        // Configure retry pipeline for transient Azure Storage errors
        _retryPipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(200),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder().Handle<RequestFailedException>(ex =>
                    ex.Status == 408 || // Request Timeout
                    ex.Status == 429 || // Too Many Requests
                    ex.Status == 500 || // Internal Server Error
                    ex.Status == 502 || // Bad Gateway
                    ex.Status == 503 || // Service Unavailable
                    ex.Status == 504    // Gateway Timeout
                ),
                OnRetry = args =>
                {
                    _logger.LogWarning(
                        "Azure Storage operation retrying. Attempt={Attempt} Delay={Delay}ms Status={Status}",
                        args.AttemptNumber,
                        args.RetryDelay.TotalMilliseconds,
                        args.Outcome.Exception is RequestFailedException rfe ? rfe.Status : 0);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    public async Task AddAsync(ObservationEvent observation, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("Storage.AddObservation");
        try
        {
            await _retryPipeline.ExecuteAsync(async ct =>
            {
                await _tableClient.CreateIfNotExistsAsync(ct);

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

                await _tableClient.AddEntityAsync(entity, ct);
            }, cancellationToken);

            _logger.LogInformation(
                "Observation storage write completed. SubjectId={SubjectId} PartitionKey={PartitionKey} TraceId={TraceId}",
                observation.SubjectId,
                DateOnly.FromDateTime(observation.ObservedAtUtc.UtcDateTime).ToString("yyyyMMdd"),
                Activity.Current?.TraceId.ToString());
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex,
                "Storage write failed after retries — check Azurite/Azure connection. SubjectId={SubjectId} AzureStatus={Status} AzureErrorCode={ErrorCode} Detail={Detail}",
                observation.SubjectId, ex.Status, ex.ErrorCode, ex.Message);
            throw;
        }
    }

    public async Task<IReadOnlyList<ObservationEvent>> GetByDateAsync(DateOnly date, CancellationToken cancellationToken)
    {
        try
        {
            var items = await _retryPipeline.ExecuteAsync(async ct =>
            {
                await _tableClient.CreateIfNotExistsAsync(ct);

                var partitionKey = date.ToString("yyyyMMdd");
                var results = new List<ObservationEvent>();

                await foreach (var entity in _tableClient.QueryAsync<TableEntity>(
                                   filter: $"PartitionKey eq '{partitionKey}'",
                                   cancellationToken: ct))
                {
                    results.Add(Map(entity));
                }

                return results.OrderBy(x => x.ObservedAtUtc).ToList();
            }, cancellationToken);

            _logger.LogDebug(
                "Observation chapter read completed. Date={Date} Count={Count} TraceId={TraceId}",
                date,
                items.Count,
                Activity.Current?.TraceId.ToString());

            return items;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex,
                "Storage read failed — check Azurite/Azure connection. Date={Date} AzureStatus={Status} AzureErrorCode={ErrorCode} Detail={Detail}",
                date, ex.Status, ex.ErrorCode, ex.Message);
            throw;
        }
    }

    public async Task<IReadOnlyList<ObservationEvent>> GetByDateRangeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var fromKey = from.ToString("yyyyMMdd");
        var toKey = to.ToString("yyyyMMdd");
        var items = new List<ObservationEvent>();

        // PartitionKey is yyyyMMdd — range filter uses string comparison which matches lexicographic ordering
        await foreach (var entity in _tableClient.QueryAsync<TableEntity>(
                           filter: $"PartitionKey ge '{fromKey}' and PartitionKey le '{toKey}'",
                           cancellationToken: cancellationToken))
        {
            items.Add(Map(entity));
        }

        _logger.LogDebug(
            "Date range read completed. From={From} To={To} Count={Count}",
            from,
            to,
            items.Count);

        return items.OrderBy(x => x.ObservedAtUtc).ToList();
    }

    public async Task<ObservationEvent?> GetLatestForSubjectAsync(string subjectId, CancellationToken cancellationToken)
    {
        try
        {
            var item = await _retryPipeline.ExecuteAsync(async ct =>
            {
                await _tableClient.CreateIfNotExistsAsync(ct);

                var safeSubjectId = subjectId.Replace("'", "''");
                var items = new List<ObservationEvent>();

                await foreach (var entity in _tableClient.QueryAsync<TableEntity>(
                                   filter: $"SubjectId eq '{safeSubjectId}'",
                                   cancellationToken: ct))
                {
                    items.Add(Map(entity));
                }

                return items.OrderByDescending(x => x.ObservedAtUtc).FirstOrDefault();
            }, cancellationToken);

            return item;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex,
                "Storage query failed — check Azurite/Azure connection. SubjectId={SubjectId} AzureStatus={Status} AzureErrorCode={ErrorCode} Detail={Detail}",
                subjectId, ex.Status, ex.ErrorCode, ex.Message);
            throw;
        }
    }

    public async Task<IReadOnlyList<ObservationEvent>> GetBySubjectAndDateRangeAsync(
        string subjectId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        try
        {
            var items = await _retryPipeline.ExecuteAsync(async ct =>
            {
                await _tableClient.CreateIfNotExistsAsync(ct);

                var safeSubjectId = subjectId.Replace("'", "''");
                var fromKey = from.ToString("yyyyMMdd");
                var toKey = to.ToString("yyyyMMdd");
                var results = new List<ObservationEvent>();

                // Query across date range for specific subject
                await foreach (var entity in _tableClient.QueryAsync<TableEntity>(
                                   filter: $"SubjectId eq '{safeSubjectId}' and PartitionKey ge '{fromKey}' and PartitionKey le '{toKey}'",
                                   cancellationToken: ct))
                {
                    results.Add(Map(entity));
                }

                return results.OrderBy(x => x.ObservedAtUtc).ToList();
            }, cancellationToken);

            _logger.LogDebug(
                "Filtered observation read completed. SubjectId={SubjectId} From={From} To={To} Count={Count}",
                subjectId,
                from,
                to,
                items.Count);

            return items;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex,
                "Storage filtered query failed. SubjectId={SubjectId} From={From} To={To} AzureStatus={Status} AzureErrorCode={ErrorCode} Detail={Detail}",
                subjectId, from, to, ex.Status, ex.ErrorCode, ex.Message);
            throw;
        }
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
