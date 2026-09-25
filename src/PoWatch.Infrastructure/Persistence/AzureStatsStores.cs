using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using PoWatch.Application.Contracts;
using PoWatch.Application.Options;
using PoWatch.Domain.Models;
using PoWatch.Domain.Services;

namespace PoWatch.Infrastructure.Persistence;

/// <summary>
/// Rollups partitioned by user and grain, one row per bucket. Merges are read-merge-write with an
/// ETag check, retried on conflict, so two ingests landing in the same minute both count.
/// </summary>
public sealed class AzureRollupStore(AzureStorageClients clients, IOptions<AzureStorageOptions> options) : IRollupStore
{
    private const int MaxAttempts = 10;

    private readonly TableClient _table = clients.TableService.GetTableClient(options.Value.RollupsTable);

    public async Task MergeAsync(string userId, RollupGrain grain, DateTimeOffset bucketStartUtc, Rollup delta, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delta);
        var partition = Partition(userId, grain);
        var rowKey = StatsEntityMapper.Ticks(bucketStartUtc);

        for (var attempt = 1; ; attempt++)
        {
            var existing = await _table.GetEntityIfExistsAsync<TableEntity>(partition, rowKey, cancellationToken: cancellationToken);
            var current = existing.HasValue ? RollupMapper.ToRollup(existing.Value!) : Rollup.Empty;
            var merged = RollupMapper.ToEntity(partition, rowKey, Rollup.Merge(current, delta) with { BucketStartUtc = bucketStartUtc });

            try
            {
                if (existing.HasValue)
                    await _table.UpdateEntityAsync(merged, existing.Value!.ETag, TableUpdateMode.Replace, cancellationToken);
                else
                    await _table.AddEntityAsync(merged, cancellationToken);
                return;
            }
            catch (RequestFailedException ex) when (ex.Status is 409 or 412 && attempt < MaxAttempts)
            {
                // Someone else merged into this bucket first; re-read and merge on top of theirs.
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(5, 25 * attempt)), cancellationToken);
            }
        }
    }

    public async Task<IReadOnlyList<Rollup>> GetRangeAsync(string userId, RollupGrain grain, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken)
    {
        var partition = Partition(userId, grain);
        var from = StatsEntityMapper.Ticks(fromUtc);
        var to = StatsEntityMapper.Ticks(toUtc);
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {partition} and RowKey ge {from} and RowKey lt {to}");

        var rollups = new List<Rollup>();
        await foreach (var entity in _table.QueryAsync<TableEntity>(filter, cancellationToken: cancellationToken))
            rollups.Add(RollupMapper.ToRollup(entity));
        return rollups;
    }

    public async Task<Rollup> GetAllTimeAsync(string userId, CancellationToken cancellationToken)
    {
        var response = await _table.GetEntityIfExistsAsync<TableEntity>(
            Partition(userId, RollupGrain.AllTime), StatsEntityMapper.Ticks(DateTimeOffset.UnixEpoch), cancellationToken: cancellationToken);
        return response.HasValue ? RollupMapper.ToRollup(response.Value!) : Rollup.Empty;
    }

    private static string Partition(string userId, RollupGrain grain) => $"{StatsEntityMapper.UserKey(userId)}|{grain}";
}

/// <summary>Unlocks (row key "a:{id}") and records ("r:{id}") in one partition per user.</summary>
public sealed class AzureAchievementStore(AzureStorageClients clients, IOptions<AzureStorageOptions> options) : IAchievementStore
{
    private readonly TableClient _table = clients.TableService.GetTableClient(options.Value.AchievementsTable);

    public async Task<IReadOnlyDictionary<string, DateTimeOffset>> GetUnlockedAsync(string userId, CancellationToken cancellationToken)
    {
        var unlocked = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        await foreach (var entity in QueryAsync(userId, "a:", cancellationToken))
            unlocked[entity.RowKey[2..]] = entity.GetDateTimeOffset("UnlockedAtUtc")!.Value;
        return unlocked;
    }

    public async Task UnlockAsync(string userId, IReadOnlyList<AchievementUnlock> unlocks, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unlocks);
        foreach (var unlock in unlocks)
        {
            try
            {
                await _table.AddEntityAsync(
                    new TableEntity(StatsEntityMapper.UserKey(userId), $"a:{unlock.AchievementId}") { ["UnlockedAtUtc"] = unlock.UnlockedAtUtc },
                    cancellationToken);
            }
            catch (RequestFailedException ex) when (ex.Status == 409)
            {
                // Already unlocked: the first unlock time stands.
            }
        }
    }

    public async Task<IReadOnlyDictionary<string, RecordEntry>> GetRecordsAsync(string userId, CancellationToken cancellationToken)
    {
        var records = new Dictionary<string, RecordEntry>(StringComparer.Ordinal);
        await foreach (var entity in QueryAsync(userId, "r:", cancellationToken))
        {
            var id = entity.RowKey[2..];
            records[id] = new RecordEntry(id, entity.GetDouble("Value") ?? 0, entity.GetDateTimeOffset("SetAtUtc")!.Value, entity.GetDouble("PreviousValue"));
        }
        return records;
    }

    public async Task SaveRecordsAsync(string userId, IReadOnlyList<RecordEntry> records, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);
        foreach (var record in records)
        {
            var entity = new TableEntity(StatsEntityMapper.UserKey(userId), $"r:{record.RecordId}")
            {
                ["Value"] = record.Value,
                ["SetAtUtc"] = record.SetAtUtc
            };
            if (record.PreviousValue is { } previous) entity["PreviousValue"] = previous;
            await _table.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
        }
    }

    private AsyncPageable<TableEntity> QueryAsync(string userId, string prefix, CancellationToken cancellationToken)
    {
        // Row keys sort lexically, so a prefix range is [prefix, prefix + '~').
        var filter = TableClient.CreateQueryFilter(
            $"PartitionKey eq {StatsEntityMapper.UserKey(userId)} and RowKey ge {prefix} and RowKey lt {prefix + "~"}");
        return _table.QueryAsync<TableEntity>(filter, cancellationToken: cancellationToken);
    }
}

/// <summary>Rollup ↔ entity. Empty stats omit min/max so no infinities reach the table.</summary>
internal static class RollupMapper
{
    public static TableEntity ToEntity(string partitionKey, string rowKey, Rollup r)
    {
        var entity = new TableEntity(partitionKey, rowKey)
        {
            ["BucketStartUtc"] = r.BucketStartUtc,
            ["Ticks"] = r.Ticks,
            ["Seconds"] = r.Seconds,
            ["PixelSamples"] = r.PixelSamples,
            ["DetectorSamples"] = r.DetectorSamples,
            ["OccupiedTicks"] = r.OccupiedTicks,
            ["PeakConcurrency"] = r.PeakConcurrency,
            ["Grid"] = StatsEntityMapper.Pack(r.Grid.Span),
            ["PresenceGrid"] = StatsEntityMapper.Pack(r.PresenceGrid.Span),
            ["Classes"] = JsonSerializer.Serialize(r.Classes),
            ["Palette"] = JsonSerializer.Serialize(r.Palette),
            ["Visits"] = r.Visits,
            ["DwellHistogram"] = JsonSerializer.Serialize(r.DwellHistogram),
            ["DwellMaxSeconds"] = r.DwellMaxSeconds,
            ["Entries"] = JsonSerializer.Serialize(r.Entries),
            ["Exits"] = JsonSerializer.Serialize(r.Exits)
        };
        WriteStat(entity, "Motion", r.Motion);
        WriteStat(entity, "MotionPeak", r.MotionPeak);
        WriteStat(entity, "Luminance", r.Luminance);
        return entity;
    }

    public static Rollup ToRollup(TableEntity e) => new()
    {
        BucketStartUtc = e.GetDateTimeOffset("BucketStartUtc")!.Value,
        Ticks = e.GetInt64("Ticks") ?? 0,
        Seconds = e.GetDouble("Seconds") ?? 0,
        PixelSamples = e.GetInt64("PixelSamples") ?? 0,
        DetectorSamples = e.GetInt64("DetectorSamples") ?? 0,
        OccupiedTicks = e.GetInt64("OccupiedTicks") ?? 0,
        PeakConcurrency = e.GetInt32("PeakConcurrency") ?? 0,
        Motion = ReadStat(e, "Motion"),
        MotionPeak = ReadStat(e, "MotionPeak"),
        Luminance = ReadStat(e, "Luminance"),
        Grid = StatsEntityMapper.Unpack(e.GetBinary("Grid")),
        PresenceGrid = StatsEntityMapper.Unpack(e.GetBinary("PresenceGrid")),
        Classes = StatsEntityMapper.Json<Dictionary<string, ClassTotals>>(e, "Classes") ?? [],
        Palette = StatsEntityMapper.Json<Dictionary<int, long>>(e, "Palette") ?? [],
        Visits = e.GetInt64("Visits") ?? 0,
        DwellHistogram = StatsEntityMapper.Json<Dictionary<int, long>>(e, "DwellHistogram") ?? [],
        DwellMaxSeconds = e.GetDouble("DwellMaxSeconds") ?? 0,
        Entries = StatsEntityMapper.Json<Dictionary<FrameEdge, long>>(e, "Entries") ?? [],
        Exits = StatsEntityMapper.Json<Dictionary<FrameEdge, long>>(e, "Exits") ?? []
    };

    private static void WriteStat(TableEntity entity, string name, RunningStat stat)
    {
        entity[$"{name}Count"] = stat.Count;
        entity[$"{name}Sum"] = stat.Sum;
        entity[$"{name}SumSquares"] = stat.SumSquares;
        if (stat.Count == 0) return;
        entity[$"{name}Min"] = stat.Min;
        entity[$"{name}Max"] = stat.Max;
    }

    private static RunningStat ReadStat(TableEntity e, string name)
    {
        var count = e.GetInt64($"{name}Count") ?? 0;
        return count == 0
            ? RunningStat.Empty
            : new RunningStat(count, e.GetDouble($"{name}Sum") ?? 0, e.GetDouble($"{name}SumSquares") ?? 0,
                e.GetDouble($"{name}Min") ?? 0, e.GetDouble($"{name}Max") ?? 0);
    }
}
