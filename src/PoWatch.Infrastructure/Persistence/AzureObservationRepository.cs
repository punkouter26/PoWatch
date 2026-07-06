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

                try
                {
                    await _tableClient.AddEntityAsync(entity, ct);
                }
                catch (RequestFailedException conflict) when (conflict.Status == 409)
                {
                    // Idempotent no-op: the row already exists (a Polly retry after a write that actually
                    // landed, or a client resubmit of the same idempotency key). Treat as success rather
                    // than surfacing a 409 for a write that is, in effect, already durable.
                    _logger.LogInformation(
                        "Observation already persisted — idempotent duplicate ignored. SubjectId={SubjectId} RowKey={RowKey}",
                        observation.SubjectId, rowKey);
                }
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

        // Wrapped in the retry pipeline (previously bypassed it) — this feeds the Drift Radar bulk load,
        // so a transient storage blip here would otherwise fail the whole multi-subject drift computation.
        var items = await _retryPipeline.ExecuteAsync(async ct =>
        {
            var results = new List<ObservationEvent>();

            // PartitionKey is yyyyMMdd — range filter uses string comparison which matches lexicographic ordering
            await foreach (var entity in _tableClient.QueryAsync<TableEntity>(
                               filter: $"PartitionKey ge '{fromKey}' and PartitionKey le '{toKey}'",
                               cancellationToken: ct))
            {
                results.Add(Map(entity));
            }

            return results.OrderBy(x => x.ObservedAtUtc).ToList();
        }, cancellationToken);

        _logger.LogDebug(
            "Date range read completed. From={From} To={To} Count={Count}",
            from,
            to,
            items.Count);

        return items;
    }

    public async Task<ObservationEvent?> GetLatestForSubjectAsync(string subjectId, CancellationToken cancellationToken)
    {
        // Scoped partition scan (30 days, newest first) replaces the previous full cross-partition
        // table scan which loaded ALL rows just to find one event. Stop as soon as a hit is found.
        var safeSubjectId = subjectId.Replace("'", "''");
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);

        try
        {
            for (var dayOffset = 0; dayOffset < 30; dayOffset++)
            {
                var partitionKey = today.AddDays(-dayOffset).ToString("yyyyMMdd");
                var results = new List<ObservationEvent>();

                await foreach (var entity in _tableClient.QueryAsync<TableEntity>(
                                   filter: $"PartitionKey eq '{partitionKey}' and SubjectId eq '{safeSubjectId}'",
                                   cancellationToken: cancellationToken))
                {
                    results.Add(Map(entity));
                }

                if (results.Count > 0)
                    return results.OrderByDescending(x => x.ObservedAtUtc).First();
            }

            return null;
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

        var safeSubjectId = oldSubjectId.Replace("'", "''");

        // Wrapped in the retry pipeline and made resumable: upsert-Replace is idempotent and the query
        // re-reads only rows still under the old id, so a transient fault mid-rewrite retries cleanly
        // and converges rather than aborting the identity operation halfway.
        var count = await _retryPipeline.ExecuteAsync(async ct =>
        {
            var entities = new List<TableEntity>();
            await foreach (var entity in _tableClient.QueryAsync<TableEntity>(
                               filter: $"SubjectId eq '{safeSubjectId}'",
                               cancellationToken: ct))
            {
                entities.Add(entity);
            }

            foreach (var entity in entities)
            {
                var updated = RewriteEntity(entity, target);

                await _tableClient.UpsertEntityAsync(updated, TableUpdateMode.Replace, ct);

                if (!string.Equals(entity.RowKey, updated.RowKey, StringComparison.OrdinalIgnoreCase))
                {
                    await _tableClient.DeleteEntityAsync(entity.PartitionKey, entity.RowKey, entity.ETag, cancellationToken: ct);
                }
            }

            return entities.Count;
        }, cancellationToken);

        _logger.LogInformation(
            "Subject history rewrite completed. OldSubjectId={OldSubjectId} CanonicalSubjectId={CanonicalSubjectId} EventsRewritten={EventsRewritten} TraceId={TraceId}",
            oldSubjectId,
            target.SubjectId,
            count,
            Activity.Current?.TraceId.ToString());

        return count;
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
