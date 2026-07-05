using System.Net;
using System.Net.Http.Json;
using PoWatch.Shared.Models;
using PoWatch.Domain.Models;

namespace PoWatch.Tests;

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

        var state = await response.Content.ReadFromJsonAsync<ObserverRuntimeStateDto>();
        Assert.NotNull(state);
        Assert.True(state.ObservationLoopEnabled);
        Assert.Equal(10, state.PollIntervalSeconds);
    }

    [Fact]
    public async Task IngestingDuplicateActivity_CreatesBothEntriesWithRedundancyFlag()
    {
        // First observation
        var firstResult = await _client.PostAsJsonAsync("/api/observer/ingest", new IngestObservationRequestDto
        {
            SubjectHint = "Kim",
            Activity = "Desk Work",
            ClinicalPayload = "<S>Kim resumed desk work.<E>"
        });
        var firstResponse = await firstResult.Content.ReadFromJsonAsync<IngestObservationResultDto>();
        Assert.NotNull(firstResponse);
        Assert.True(firstResponse.Accepted);

        // Second observation (same activity = redundant, but STILL persisted per Fix #7)
        var secondResult = await _client.PostAsJsonAsync("/api/observer/ingest", new IngestObservationRequestDto
        {
            SubjectHint = "Kim",
            Activity = "Desk Work",
            ClinicalPayload = "<S>Kim is still working at the same desk.<E>"
        });
        var secondResponse = await secondResult.Content.ReadFromJsonAsync<IngestObservationResultDto>();
        Assert.NotNull(secondResponse);
        Assert.True(secondResponse.Accepted);
        // Redundant observation is still persisted, but flagged
        Assert.True(secondResponse.SkippedAsRedundant);

        // Both events should appear in the timeline (Fix #7: always persist)
        var chapter = await _client.GetFromJsonAsync<DailyChapter>($"/api/archives/{DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}");

        Assert.NotNull(chapter);
        // Both "Desk Work" events for Kim are now persisted
        Assert.Equal(2, chapter.Timeline.Count(x => x.SubjectDisplayName == "Kim" && x.Activity == "Desk Work"));
    }

    [Fact]
    public async Task AcknowledgeEndpoint_ReturnsSuccess()
    {
        var response = await _client.PostAsJsonAsync("/api/observer/acknowledge", new
        {
            EventIds = new[] { Guid.NewGuid().ToString("N") },
            AcknowledgedBy = "nurse-smith"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
