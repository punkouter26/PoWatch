using System.Net;
using System.Net.Http.Json;
using PoWatch.Application.Models;
using PoWatch.Domain.Models;

namespace PoWatch.IntegrationTests;

public sealed class ObserverIngestFlowTests : IClassFixture<AzuriteWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ObserverIngestFlowTests(AzuriteWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ObserverState_ReturnsConfiguredRuntimeFlags()
    {
        var response = await _client.GetAsync("/api/observer/state");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var state = await response.Content.ReadFromJsonAsync<ObserverRuntimeState>();
        Assert.NotNull(state);
        Assert.True(state.ObservationLoopEnabled);
        Assert.Equal(10, state.PollIntervalSeconds);
    }

    [Fact]
    public async Task IngestingDuplicateActivity_DoesNotCreateDuplicateTimelineEntry()
    {
        await _client.PostAsJsonAsync("/api/observer/ingest", new IngestObservationRequest
        {
            SubjectHint = "Kim",
            Activity = "Desk Work",
            ClinicalPayload = "<S>Kim resumed desk work.<E>"
        });

        await _client.PostAsJsonAsync("/api/observer/ingest", new IngestObservationRequest
        {
            SubjectHint = "Kim",
            Activity = "Desk Work",
            ClinicalPayload = "<S>Kim is still working at the same desk.<E>"
        });

        var chapter = await _client.GetFromJsonAsync<DailyChapter>($"/api/archives/{DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}");

        Assert.NotNull(chapter);
        Assert.Single(chapter.Timeline, x => x.SubjectDisplayName == "Kim" && x.Activity == "Desk Work");
    }
}
