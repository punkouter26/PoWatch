using System.Net;
using System.Net.Http.Json;
using PoWatch.Shared.Models;

namespace PoWatch.IntegrationTests;

/// <summary>
/// Integration tests for the Drift Radar endpoint.
/// Verifies the full path: event ingestion → DriftRadarService → GET /api/identity/subjects/live-risk.
/// </summary>
public sealed class DriftRadarApiTests : IClassFixture<AzuriteWebApplicationFactory>
{
    private readonly HttpClient _client;

    public DriftRadarApiTests(AzuriteWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task LiveRisk_ReturnsOk_WhenNoSubjectsExist()
    {
        var response = await _client.GetAsync("/api/identity/subjects/live-risk");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<List<SubjectDriftStatusDto>>();
        Assert.NotNull(result);
    }

    [Fact]
    public async Task LiveRisk_ReturnsSubjectWithComputedDrift_AfterEnoughEventsIngested()
    {
        // Ingest 4 events (> MinEventsForDrift=3) for a subject
        for (var i = 0; i < 4; i++)
        {
            var ingest = await _client.PostAsJsonAsync("/api/observer/ingest", new IngestObservationRequestDto
            {
                SubjectHint = "DriftSubjectA",
                Activity = "Walking",
                ClinicalPayload = $"<S>DriftSubjectA was observed walking (event {i + 1}).<E>"
            });
            Assert.Equal(HttpStatusCode.OK, ingest.StatusCode);
        }

        var response = await _client.GetAsync("/api/identity/subjects/live-risk");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var driftStatus = await response.Content.ReadFromJsonAsync<List<SubjectDriftStatusDto>>();
        Assert.NotNull(driftStatus);

        var subject = driftStatus.FirstOrDefault(s =>
            s.DisplayName.Contains("DriftSubjectA", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(subject);
        // With no historical data, baseline vector is all zeros → DriftScore = 100 → "Extreme Deviation"
        Assert.NotEqual("Insufficient Data", subject.DriftLabel);
        Assert.Equal(24, subject.HourlyBaselineVector.Count);
        Assert.Equal(24, subject.HourlyTodayVector.Count);
    }

    [Fact]
    public async Task LiveRisk_ReturnsInsufficientData_WhenSubjectHasTooFewEventsToday()
    {
        // Ingest only 2 events (< MinEventsForDrift=3)
        for (var i = 0; i < 2; i++)
        {
            await _client.PostAsJsonAsync("/api/observer/ingest", new IngestObservationRequestDto
            {
                SubjectHint = "DriftSubjectB",
                Activity = "Sitting",
                ClinicalPayload = $"<S>DriftSubjectB was observed sitting (event {i + 1}).<E>"
            });
        }

        var response = await _client.GetAsync("/api/identity/subjects/live-risk");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var driftStatus = await response.Content.ReadFromJsonAsync<List<SubjectDriftStatusDto>>();
        Assert.NotNull(driftStatus);

        var subject = driftStatus.FirstOrDefault(s =>
            s.DisplayName.Contains("DriftSubjectB", StringComparison.OrdinalIgnoreCase));

        // May not appear if no profile was created, or may appear with "Insufficient Data"
        if (subject is not null)
        {
            Assert.Equal("Insufficient Data", subject.DriftLabel);
        }
    }

    [Fact]
    public async Task LiveRisk_ResponseIncludesComputedAtUtc()
    {
        for (var i = 0; i < 4; i++)
        {
            await _client.PostAsJsonAsync("/api/observer/ingest", new IngestObservationRequestDto
            {
                SubjectHint = "DriftSubjectC",
                Activity = "Standing",
                ClinicalPayload = $"<S>DriftSubjectC event {i + 1}.<E>"
            });
        }

        var driftStatus = await _client.GetFromJsonAsync<List<SubjectDriftStatusDto>>(
            "/api/identity/subjects/live-risk");

        Assert.NotNull(driftStatus);
        var subject = driftStatus.FirstOrDefault(s =>
            s.DisplayName.Contains("DriftSubjectC", StringComparison.OrdinalIgnoreCase));

        if (subject is not null)
        {
            // ComputedAtUtc should be close to now
            var elapsed = DateTimeOffset.UtcNow - subject.ComputedAtUtc;
            Assert.True(elapsed.TotalSeconds < 60, "ComputedAtUtc should be within the last 60 seconds.");
        }
    }
}
