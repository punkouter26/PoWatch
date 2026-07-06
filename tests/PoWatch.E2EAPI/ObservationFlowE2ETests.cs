using System.Net;
using System.Net.Http.Json;
using PoWatch.Shared.Models;

namespace PoWatch.Tests.E2E;

/// <summary>
/// Pure API-call E2E scenarios emulating client → server system functionality.
/// </summary>
public sealed class ObservationFlowE2ETests(ApiE2EFactory factory) : IClassFixture<ApiE2EFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Health_and_diag_endpoints_are_live()
    {
        var health = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        var diag = await _client.GetAsync("/diag");
        Assert.Equal(HttpStatusCode.OK, diag.StatusCode);
    }

    [Fact]
    public async Task Ingest_then_read_state_reflects_a_persisted_observation()
    {
        var request = new IngestObservationRequestDto
        {
            SubjectHint = "e2e-subject",
            Activity = "seated at desk",
            ClinicalPayload = "<SUBJECT:e2e-subject> One person seated at a desk.",
            IsSignificant = false
        };

        var ingest = await _client.PostAsJsonAsync("/api/observer/ingest", request);
        Assert.Equal(HttpStatusCode.OK, ingest.StatusCode);

        var result = await ingest.Content.ReadFromJsonAsync<IngestObservationResultDto>();
        Assert.NotNull(result);
        Assert.True(result!.Accepted);
        Assert.False(string.IsNullOrWhiteSpace(result.SubjectId));

        var state = await _client.GetFromJsonAsync<ObserverRuntimeStateDto>("/api/observer/state");
        Assert.NotNull(state);
        Assert.True(state!.ObservationLoopEnabled);
    }
}
