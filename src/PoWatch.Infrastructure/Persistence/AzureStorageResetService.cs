using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PoWatch.Application.Contracts;
using PoWatch.Application.Options;
using PoWatch.Domain.Services;
using PoWatch.Shared.Models;
using System.Diagnostics;

namespace PoWatch.Infrastructure.Persistence;

/// <summary>
/// Deletes every row from both Azure Table Storage tables and every blob from the
/// significant-images container. Intended for development/test resets only; guard
/// with the <c>AllowDataReset</c> feature flag at the endpoint layer.
/// </summary>
public sealed class AzureStorageResetService(
    AzureStorageClients clients,
    IOptions<AzureStorageOptions> options,
    ILogger<AzureStorageResetService> logger) : IStorageResetService
{
    public async Task<StorageResetResultDto> ResetAllAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var opts = options.Value;

        logger.LogWarning(
            "Storage reset initiated. Tables=[{ObsTable},{SubjTable}] Container={Container}",
            opts.ObservationsTable, opts.SubjectsTable, opts.SignificantImagesContainer);

        try
        {
            var obsTable = clients.TableService.GetTableClient(opts.ObservationsTable);
            var subjTable = clients.TableService.GetTableClient(opts.SubjectsTable);
            var blobContainer = clients.BlobService.GetBlobContainerClient(opts.SignificantImagesContainer);

            var obsDeleted = await DeleteAllTableRowsAsync(obsTable, cancellationToken);
            logger.LogDebug("Observations deleted. Count={Count}", obsDeleted);

            var subjDeleted = await DeleteAllTableRowsAsync(subjTable, cancellationToken);
            logger.LogDebug("Subjects deleted. Count={Count}", subjDeleted);

            var blobsDeleted = await DeleteAllBlobsAsync(blobContainer, cancellationToken);
            logger.LogDebug("Blobs deleted. Count={Count}", blobsDeleted);

            // Clear in-memory slug registry so renamed subjects don't collide after a re-seed.
            SubjectIdSlugger.Reset();
            logger.LogDebug("SubjectIdSlugger registry cleared.");

            sw.Stop();
            logger.LogWarning(
                "Storage reset complete. ObservationsDeleted={Obs} SubjectsDeleted={Subj} BlobsDeleted={Blobs} Duration={Duration}ms",
                obsDeleted, subjDeleted, blobsDeleted, sw.ElapsedMilliseconds);

            return new StorageResetResultDto
            {
                Success = true,
                ObservationsDeleted = obsDeleted,
                SubjectsDeleted = subjDeleted,
                BlobsDeleted = blobsDeleted,
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex,
                "Storage reset failed after {Duration}ms. ErrorType={ErrorType} Detail={Detail}",
                sw.ElapsedMilliseconds, ex.GetType().Name, ex.Message);

            return new StorageResetResultDto
            {
                Success = false,
                Duration = sw.Elapsed,
                ErrorMessage = ex.Message
            };
        }
    }

    private static async Task<int> DeleteAllTableRowsAsync(
        TableClient tableClient, CancellationToken cancellationToken)
    {
        var deleted = 0;
        var batch = new List<TableTransactionAction>();
        var currentPartition = string.Empty;

        await foreach (var entity in tableClient.QueryAsync<TableEntity>(
            select: ["PartitionKey", "RowKey"],
            cancellationToken: cancellationToken))
        {
            // Azure Table Storage transactions require all actions share the same partition key.
            if (entity.PartitionKey != currentPartition && batch.Count > 0)
            {
                deleted += await FlushBatchAsync(tableClient, batch, cancellationToken);
                batch.Clear();
            }

            currentPartition = entity.PartitionKey;
            batch.Add(new TableTransactionAction(TableTransactionActionType.Delete, entity));

            // Maximum 100 operations per batch transaction.
            if (batch.Count == 100)
            {
                deleted += await FlushBatchAsync(tableClient, batch, cancellationToken);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
            deleted += await FlushBatchAsync(tableClient, batch, cancellationToken);

        return deleted;
    }

    private static async Task<int> FlushBatchAsync(
        TableClient tableClient, List<TableTransactionAction> batch, CancellationToken cancellationToken)
    {
        await tableClient.SubmitTransactionAsync(batch, cancellationToken);
        return batch.Count;
    }

    private static async Task<int> DeleteAllBlobsAsync(
        Azure.Storage.Blobs.BlobContainerClient containerClient, CancellationToken cancellationToken)
    {
        var deleted = 0;
        await foreach (var blob in containerClient.GetBlobsAsync(cancellationToken: cancellationToken))
        {
            await containerClient.DeleteBlobIfExistsAsync(blob.Name, cancellationToken: cancellationToken);
            deleted++;
        }
        return deleted;
    }
}
