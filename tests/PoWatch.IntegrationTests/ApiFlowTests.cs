using System.Net;
using System.Net.Http.Json;
using PoWatch.Application.Models;
using PoWatch.Domain.Models;

namespace PoWatch.IntegrationTests;

public sealed class ApiFlowTests : IClassFixture<AzuriteWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ApiFlowTests(AzuriteWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task IngestAndArchiveFlow_ReturnsPersistedData()
    {
        var ingestResponse = await _client.PostAsJsonAsync("/api/observer/ingest", new IngestObservationRequest
        {
            SubjectHint = "Kim",
            Activity = "Desk Work",
            ClinicalPayload = "<S>Known subject entered and resumed desk work.<E>",
            IsSignificant = true,
            SignificantReason = "Known subject change"
        });

        Assert.Equal(HttpStatusCode.OK, ingestResponse.StatusCode);

        var chapter = await _client.GetFromJsonAsync<DailyChapter>($"/api/archives/{DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}");

        Assert.NotNull(chapter);
        Assert.NotEmpty(chapter.Timeline);
        Assert.Contains(chapter.Timeline, x => x.SubjectDisplayName == "Kim");
    }

    [Fact]
    public async Task MergeIdentity_CompactsSubjectHistory()
    {
        await _client.PostAsJsonAsync("/api/observer/ingest", new IngestObservationRequest
        {
            SubjectHint = "Subject-4",
            Activity = "Entered",
            ClinicalPayload = "<S>Unknown subject entered room.<E>",
            IsSignificant = true,
            SignificantReason = "New entity"
        });

        await _client.PostAsJsonAsync("/api/observer/ingest", new IngestObservationRequest
        {
            SubjectHint = "Subject-7",
            Activity = "Entered",
            ClinicalPayload = "<S>Unknown subject entered room.<E>",
            IsSignificant = true,
            SignificantReason = "New entity"
        });

        var mergeResponse = await _client.PostAsJsonAsync("/api/identity/merge", new MergeIdentityRequest
        {
            PrimarySubjectId = "Subject-4",
            SecondarySubjectId = "Subject-7",
            NewDisplayName = "Maya"
        });

        Assert.Equal(HttpStatusCode.OK, mergeResponse.StatusCode);

        var subjects = await _client.GetFromJsonAsync<List<SubjectProfile>>("/api/identity/subjects");
        Assert.NotNull(subjects);
        Assert.DoesNotContain(subjects, s => s.SubjectId == "Subject-7");
        Assert.DoesNotContain(subjects, s => s.SubjectId == "Subject-4");
        Assert.Contains(subjects, s => s.SubjectId == "maya" && s.DisplayName == "Maya");

        var chapter = await _client.GetFromJsonAsync<DailyChapter>($"/api/archives/{DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}");
        Assert.NotNull(chapter);
        Assert.Contains(chapter.Timeline, s => s.SubjectId == "maya" && s.SubjectDisplayName == "Maya");
    }
}

