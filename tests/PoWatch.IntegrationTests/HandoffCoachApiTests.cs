using System.Net;
using System.Net.Http.Json;
using PoWatch.Shared.Models;

namespace PoWatch.Tests;

/// <summary>
/// Integration tests for the Handoff Coach endpoint.
/// Verifies the full path: event ingestion → HandoffCoachService → POST /api/archives/{date}/handoff-brief.
/// </summary>
public sealed class HandoffCoachApiTests : IClassFixture<AzuriteWebApplicationFactory>
{
    private readonly HttpClient _client;

    public HandoffCoachApiTests(AzuriteWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task HandoffBrief_ReturnsOk_WithTemplateBriefWhenNoAiConfigured()
    {
        // Ingest some events to give the brief meaningful data
        for (var i = 0; i < 3; i++)
        {
            await _client.PostAsJsonAsync("/api/observer/ingest", new IngestObservationRequestDto
            {
                SubjectHint = "HandoffSubjectA",
                Activity = "Desk Work",
                ClinicalPayload = $"<S>HandoffSubjectA performing desk work (event {i + 1}).<E>",
                IsSignificant = i == 0,
                SignificantReason = i == 0 ? "Start of shift activity" : null
            });
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var request = new GenerateHandoffBriefRequestDto
        {
            ShiftWindow = "FullDay",
            Audience = "NurseToNurse",
            IncludeUnresolvedAlerts = true,
            IncludeHighlights = true
        };

        var response = await _client.PostAsJsonAsync($"/api/archives/{today}/handoff-brief", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var brief = await response.Content.ReadFromJsonAsync<HandoffBriefDto>();
        Assert.NotNull(brief);
        Assert.False(string.IsNullOrWhiteSpace(brief.Summary));
        Assert.Equal("NurseToNurse", brief.Audience);
        Assert.Equal("FullDay", brief.ShiftWindow);
        // No Azure OpenAI configured in tests → template path
        Assert.False(brief.IsAiGenerated);
    }

    [Fact]
    public async Task HandoffBrief_ReturnsBriefWithSourceNotes_AfterIngestion()
    {
        for (var i = 0; i < 2; i++)
        {
            await _client.PostAsJsonAsync("/api/observer/ingest", new IngestObservationRequestDto
            {
                SubjectHint = "HandoffSubjectB",
                Activity = "Walking",
                ClinicalPayload = $"<S>HandoffSubjectB walking (event {i + 1}).<E>"
            });
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var response = await _client.PostAsJsonAsync($"/api/archives/{today}/handoff-brief",
            new GenerateHandoffBriefRequestDto
            {
                ShiftWindow = "FullDay",
                Audience = "Supervisor",
                IncludeUnresolvedAlerts = false,
                IncludeHighlights = false
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var brief = await response.Content.ReadFromJsonAsync<HandoffBriefDto>();
        Assert.NotNull(brief);
        Assert.NotEmpty(brief.SourceNotes);
        Assert.Equal("Supervisor", brief.Audience);
    }

    [Fact]
    public async Task HandoffBrief_ReturnsGeneratedAtUtcWithinReasonableWindow()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var before = DateTimeOffset.UtcNow.AddSeconds(-5);

        var response = await _client.PostAsJsonAsync($"/api/archives/{today}/handoff-brief",
            new GenerateHandoffBriefRequestDto
            {
                ShiftWindow = "FullDay",
                Audience = "NurseToNurse",
                IncludeUnresolvedAlerts = false,
                IncludeHighlights = false
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var brief = await response.Content.ReadFromJsonAsync<HandoffBriefDto>();
        Assert.NotNull(brief);
        Assert.True(brief.GeneratedAtUtc >= before, "GeneratedAtUtc should be set to approximately now.");
        Assert.True(brief.GeneratedAtUtc <= DateTimeOffset.UtcNow.AddSeconds(10));
    }

    [Fact]
    public async Task HandoffBrief_ReturnsBadRequest_WhenDateIsInvalidFormat()
    {
        var response = await _client.PostAsJsonAsync("/api/archives/not-a-date/handoff-brief",
            new GenerateHandoffBriefRequestDto { ShiftWindow = "FullDay", Audience = "NurseToNurse" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task HandoffBrief_ReturnsNurseToNurseBrief_WhenAudienceIsInvalid()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

        var response = await _client.PostAsJsonAsync($"/api/archives/{today}/handoff-brief",
            new GenerateHandoffBriefRequestDto
            {
                ShiftWindow = "FullDay",
                Audience = "UnknownAudience",
                IncludeUnresolvedAlerts = false,
                IncludeHighlights = false
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var brief = await response.Content.ReadFromJsonAsync<HandoffBriefDto>();
        Assert.NotNull(brief);
        // Invalid audience falls back to NurseToNurse
        Assert.Equal("NurseToNurse", brief.Audience);
    }
}
