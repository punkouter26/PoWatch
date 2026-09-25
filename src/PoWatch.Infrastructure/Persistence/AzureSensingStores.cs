using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using PoWatch.Application.Contracts;
using PoWatch.Application.Options;
using PoWatch.Domain.Models;

namespace PoWatch.Infrastructure.Persistence;

/// <summary>Sessions partitioned by user, one row per session id.</summary>
public sealed class AzureSessionRepository(AzureStorageClients clients, IOptions<AzureStorageOptions> options) : ISessionRepository
{
    private readonly TableClient _table = clients.TableService.GetTableClient(options.Value.SessionsTable);

    public async Task UpsertAsync(Session session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        await _table.UpsertEntityAsync(StatsEntityMapper.ToEntity(session), TableUpdateMode.Replace, cancellationToken);
    }

    public async Task<Session?> GetAsync(string userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var response = await _table.GetEntityIfExistsAsync<TableEntity>(
            StatsEntityMapper.UserKey(userId), sessionId.ToString("N"), cancellationToken: cancellationToken);
        return response.HasValue ? StatsEntityMapper.ToSession(response.Value!) : null;
    }

    public async Task<IReadOnlyList<Session>> ListAsync(string userId, int take, CancellationToken cancellationToken)
    {
        // One user's sessions are a small set; read the partition and order by start time.
        var partition = StatsEntityMapper.UserKey(userId);
        var sessions = new List<Session>();
        await foreach (var entity in _table.QueryAsync<TableEntity>(e => e.PartitionKey == partition, cancellationToken: cancellationToken))
            sessions.Add(StatsEntityMapper.ToSession(entity));

        return sessions.OrderByDescending(s => s.StartedUtc).Take(take).ToList();
    }
}

/// <summary>Raw ticks and scene events, partitioned by user and local day.</summary>
public sealed class AzureSensingLog(AzureStorageClients clients, IOptions<AzureStorageOptions> options) : ISensingLog
{
    // A table transaction holds at most 100 operations, all in one partition.
    private const int MaxBatch = 100;

    private readonly TableClient _ticks = clients.TableService.GetTableClient(options.Value.TicksTable);
    private readonly TableClient _events = clients.TableService.GetTableClient(options.Value.SceneEventsTable);

    public async Task AppendAsync(string userId, DateOnly localDay, IReadOnlyList<Tick> ticks, IReadOnlyList<SceneEvent> events, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticks);
        ArgumentNullException.ThrowIfNull(events);
        var partition = StatsEntityMapper.DayPartition(userId, localDay);

        await UpsertAllAsync(_ticks, ticks.Select(t => StatsEntityMapper.ToEntity(partition, t)), cancellationToken);
        await UpsertAllAsync(_events, events.Select(e => StatsEntityMapper.ToEntity(partition, e)), cancellationToken);
    }

    public async Task<IReadOnlyList<Tick>> GetTicksAsync(string userId, DateOnly localDay, CancellationToken cancellationToken)
    {
        var partition = StatsEntityMapper.DayPartition(userId, localDay);
        var ticks = new List<Tick>();
        await foreach (var entity in _ticks.QueryAsync<TableEntity>(e => e.PartitionKey == partition, cancellationToken: cancellationToken))
            ticks.Add(StatsEntityMapper.ToTick(entity));
        return ticks.OrderBy(t => t.StartUtc).ToList();
    }

    public async Task<IReadOnlyList<SceneEvent>> GetEventsAsync(string userId, DateOnly localDay, CancellationToken cancellationToken)
    {
        var partition = StatsEntityMapper.DayPartition(userId, localDay);
        var events = new List<SceneEvent>();
        await foreach (var entity in _events.QueryAsync<TableEntity>(e => e.PartitionKey == partition, cancellationToken: cancellationToken))
            events.Add(StatsEntityMapper.ToSceneEvent(entity));
        return events.OrderBy(e => e.AtUtc).ToList();
    }

    private static async Task UpsertAllAsync(TableClient table, IEnumerable<TableEntity> entities, CancellationToken cancellationToken)
    {
        // Duplicate keys inside one transaction are rejected, so keep the last write per row.
        var distinct = entities.GroupBy(e => e.RowKey).Select(g => g.Last()).ToList();
        foreach (var chunk in distinct.Chunk(MaxBatch))
        {
            var actions = chunk.Select(e => new TableTransactionAction(TableTransactionActionType.UpsertReplace, e));
            await table.SubmitTransactionAsync(actions, cancellationToken);
        }
    }
}

/// <summary>Batch keys that have already been folded into the rollups, one row per user and batch.</summary>
public sealed class AzureIngestLedger(AzureStorageClients clients, IOptions<AzureStorageOptions> options) : IIngestLedger
{
    private readonly TableClient _table = clients.TableService.GetTableClient(options.Value.IngestLedgerTable);

    public async Task<bool> TryClaimAsync(string userId, Guid batchKey, CancellationToken cancellationToken)
    {
        try
        {
            await _table.AddEntityAsync(
                new TableEntity(StatsEntityMapper.UserKey(userId), batchKey.ToString("N")) { ["ClaimedAtUtc"] = DateTimeOffset.UtcNow },
                cancellationToken);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            return false;
        }
    }
}
