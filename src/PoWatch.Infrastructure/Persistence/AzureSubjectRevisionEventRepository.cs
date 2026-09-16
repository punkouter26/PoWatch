using System.Globalization;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using PoWatch.Application.Contracts;
using PoWatch.Application.Options;
using PoWatch.Domain.Models;

namespace PoWatch.Infrastructure.Persistence;

/// <summary>Durable subject revision history, partitioned by canonical subject id.</summary>
public sealed class AzureSubjectRevisionEventRepository(
    AzureStorageClients clients,
    IOptions<AzureStorageOptions> options) : ISubjectRevisionEventRepository
{
    private readonly TableClient _table = clients.TableService.GetTableClient(options.Value.SubjectRevisionsTable);

    public async Task AppendAsync(SubjectRevisionEvent revision, CancellationToken cancellationToken)
    {
        var entity = new TableEntity(
            revision.SubjectId.Value.ToUpperInvariant(),
            $"{revision.OccurredAtUtc.UtcTicks.ToString("D19", CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}")
        {
            ["SubjectId"] = revision.SubjectId.Value,
            ["OccurredAtUtc"] = revision.OccurredAtUtc,
            ["Kind"] = (int)revision.Kind,
            ["Detail"] = revision.Detail,
            ["ActorUserId"] = revision.ActorUserId
        };
        await _table.AddEntityAsync(entity, cancellationToken);
    }

    public async Task<IReadOnlyList<SubjectRevisionEvent>> GetHistoryAsync(string subjectId, CancellationToken cancellationToken)
    {
        var partition = subjectId.ToUpperInvariant();
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {partition}");
        var revisions = new List<SubjectRevisionEvent>();
        await foreach (var entity in _table.QueryAsync<TableEntity>(filter, cancellationToken: cancellationToken))
        {
            revisions.Add(new SubjectRevisionEvent
            {
                SubjectId = SubjectId.From(entity.GetString("SubjectId")),
                OccurredAtUtc = entity.GetDateTimeOffset("OccurredAtUtc")!.Value,
                Kind = (SubjectRevisionKind)entity.GetInt32("Kind")!.Value,
                Detail = entity.GetString("Detail"),
                ActorUserId = entity.GetString("ActorUserId")
            });
        }
        return revisions.OrderByDescending(e => e.OccurredAtUtc).ThenBy(e => (int)e.Kind).ToList();
    }
}
