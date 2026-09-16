using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PoWatch.Application.Options;
using PoWatch.Domain.Models;
using PoWatch.Infrastructure.Persistence;

namespace PoWatch.Integration;

public sealed class SubjectRevisionPersistenceTests(AzuriteWebApplicationFactory factory)
    : IClassFixture<AzuriteWebApplicationFactory>
{
    [Fact]
    public async Task Revision_history_survives_repository_recreation_and_preserves_actor_and_order()
    {
        var clients = factory.Services.GetRequiredService<AzureStorageClients>();
        var options = factory.Services.GetRequiredService<IOptions<AzureStorageOptions>>();
        var writer = new AzureSubjectRevisionEventRepository(clients, options);
        var subjectId = SubjectId.From($"revision-{Guid.NewGuid():N}");
        var now = DateTimeOffset.UtcNow;
        foreach (var kind in new[] { SubjectRevisionKind.Created, SubjectRevisionKind.Renamed })
        {
            await writer.AppendAsync(new SubjectRevisionEvent
            {
                SubjectId = subjectId,
                Kind = kind,
                OccurredAtUtc = kind == SubjectRevisionKind.Created ? now.AddMinutes(-1) : now,
                Detail = kind.ToString(),
                ActorUserId = "caregiver"
            }, CancellationToken.None);
        }
        var reader = new AzureSubjectRevisionEventRepository(clients, options);
        var history = await reader.GetHistoryAsync(subjectId.Value.ToUpperInvariant(), CancellationToken.None);
        Assert.Equal(new[] { SubjectRevisionKind.Renamed, SubjectRevisionKind.Created }, history.Select(e => e.Kind));
        Assert.All(history, e =>
        {
            Assert.Equal(subjectId, e.SubjectId);
            Assert.Equal("caregiver", e.ActorUserId);
            Assert.Equal(e.Kind.ToString(), e.Detail);
        });
    }
}
