using Azure.Data.Tables;

namespace PoWatch.Infrastructure.Persistence;

/// <summary>Bulk deletes for purging and retention: read only the keys, then delete in partition-sized transactions.</summary>
internal static class TableDeletes
{
    // A table transaction holds at most 100 operations, all in one partition.
    public const int MaxBatch = 100;

    /// <summary>Deletes every row matching <paramref name="filter"/>; returns how many went.</summary>
    public static async Task<int> DeleteWhereAsync(TableClient table, string filter, CancellationToken cancellationToken)
    {
        var rows = new List<TableEntity>();
        await foreach (var row in table.QueryAsync<TableEntity>(filter, select: ["PartitionKey", "RowKey"], cancellationToken: cancellationToken))
            rows.Add(row);

        foreach (var chunk in rows.GroupBy(r => r.PartitionKey).SelectMany(partition => partition.Chunk(MaxBatch)))
            await table.SubmitTransactionAsync(chunk.Select(r => new TableTransactionAction(TableTransactionActionType.Delete, r)), cancellationToken);
        return rows.Count;
    }

    /// <summary>
    /// A user's partitions are "{key}" and "{key}|…" (per day, per grain). The key is URI-escaped, so it
    /// holds no '|' and the range ["{key}|", "{key}}") cannot reach another user's rows.
    /// </summary>
    public static string UserPartitions(string userId)
    {
        var key = StatsEntityMapper.UserKey(userId);
        return TableClient.CreateQueryFilter($"PartitionKey eq {key} or (PartitionKey ge {key + "|"} and PartitionKey lt {key + "}"})");
    }

    /// <summary>The user's per-day partitions "{key}|yyyyMMdd" for days before <paramref name="firstKeptDay"/>.</summary>
    public static string DaysBefore(string userId, DateOnly firstKeptDay)
    {
        var key = StatsEntityMapper.UserKey(userId);
        var bound = StatsEntityMapper.DayPartition(userId, firstKeptDay);
        return TableClient.CreateQueryFilter($"PartitionKey ge {key + "|"} and PartitionKey lt {bound}");
    }
}
