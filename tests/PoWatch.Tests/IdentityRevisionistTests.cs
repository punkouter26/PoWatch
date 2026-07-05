using System.Net;
using System.Net.Http.Json;
using PoWatch.Shared.Models;
using PoWatch.Domain.Models;

namespace PoWatch.Tests;

public sealed class IdentityRevisionistTests : IClassFixture<AzuriteWebApplicationFactory>
{
    private readonly HttpClient _client;

    public IdentityRevisionistTests(AzuriteWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task RenameAndMerge_RewriteHistoricalArchives_AndRemoveSecondarySubject()
    {
        await _client.PostAsJsonAsync("/api/observer/ingest", new IngestObservationRequestDto
        {
            Activity = "Desk Work",
            ClinicalPayload = "<S>Unknown subject at desk.<E>",
            IsSignificant = false
        });

        await _client.PostAsJsonAsync("/api/observer/ingest", new IngestObservationRequestDto
        {
            Activity = "Walking",
            ClinicalPayload = "<S>Unknown subject walking.<E>",
            IsSignificant = false
        });

        var subjects = await _client.GetFromJsonAsync<List<SubjectProfileDto>>("/api/identity/subjects");
        Assert.NotNull(subjects);

        var generated = subjects
            .Where(x => x.SubjectId.StartsWith("Subject-", StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();

        Assert.Equal(2, generated.Length);

        using var renameRequest = new HttpRequestMessage(HttpMethod.Patch, $"/api/identity/subjects/{generated[0].SubjectId}")
        {
            Content = JsonContent.Create(new RenameSubjectRequestDto { NewName = "Maya" })
        };

        var renameResponse = await _client.SendAsync(renameRequest);
        Assert.Equal(HttpStatusCode.OK, renameResponse.StatusCode);

        var mergeResponse = await _client.PostAsJsonAsync("/api/identity/merge", new MergeIdentityRequestDto
        {
            PrimarySubjectId = "maya",
            SecondarySubjectId = generated[1].SubjectId,
            NewDisplayName = "Maya"
        });

        Assert.Equal(HttpStatusCode.OK, mergeResponse.StatusCode);

        var chapter = await _client.GetFromJsonAsync<DailyChapter>($"/api/archives/{DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}");
        Assert.NotNull(chapter);
        Assert.All(chapter.Timeline, item => Assert.Equal("Maya", item.SubjectDisplayName));

        var updatedSubjects = await _client.GetFromJsonAsync<List<SubjectProfileDto>>("/api/identity/subjects");
        Assert.NotNull(updatedSubjects);
        Assert.Contains(updatedSubjects, x => x.SubjectId == "maya");
        Assert.DoesNotContain(updatedSubjects, x => x.SubjectId == generated[1].SubjectId);
    }
}
