using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using PoWatch.Application.Contracts;
using PoWatch.Application.Options;

namespace PoWatch.Infrastructure.Persistence;

/// <summary>Deletes every row and snapshot stored for one user, across all tables.</summary>
public sealed class AzureUserDataStore(AzureStorageClients clients, IOptions<AzureStorageOptions> options) : IUserDataStore
{
    // A table transaction holds at most 100 operations, all in one partition.
    private const int MaxBatch = 100;

    public async Task PurgeAsync(string userId, CancellationToken cancellationToken)
    {
        var key = StatsEntityMapper.UserKey(userId);
        // A user's partitions are "{key}" and "{key}|…" (per day, per grain). The key is URI-escaped, so
        // it holds no '|' and the range ["{key}|", "{key}}") cannot reach another user's rows.
        var filter = TableClient.CreateQueryFilter(
            $"PartitionKey eq {key} or (PartitionKey ge {key + "|"} and PartitionKey lt {key + "}"})");

        var o = options.Value;
        foreach (var name in new[] { o.SessionsTable, o.TicksTable, o.SceneEventsTable, o.IngestLedgerTable, o.RollupsTable, o.RegularsTable })
        {
            var table = clients.TableService.GetTableClient(name);
            var rows = new List<TableEntity>();
            await foreach (var row in table.QueryAsync<TableEntity>(filter, select: ["PartitionKey", "RowKey"], cancellationToken: cancellationToken))
                rows.Add(row);

            foreach (var chunk in rows.GroupBy(r => r.PartitionKey).SelectMany(partition => partition.Chunk(MaxBatch)))
                await table.SubmitTransactionAsync(chunk.Select(r => new TableTransactionAction(TableTransactionActionType.Delete, r)), cancellationToken);
        }

        var snapshots = clients.BlobService.GetBlobContainerClient(o.SnapshotsContainer);
        await foreach (var blob in snapshots.GetBlobsAsync(prefix: AzureSnapshotStore.Prefix(userId), cancellationToken: cancellationToken))
            await snapshots.DeleteBlobIfExistsAsync(blob.Name, cancellationToken: cancellationToken);
    }
}
