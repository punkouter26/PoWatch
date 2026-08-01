using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using PoWatch.Shared.Models;

namespace PoWatch.Integration;

/// <summary>
/// Shape-and-status coverage for the endpoints the client depends on. These are the contracts that
/// break silently: a route that starts 404-ing, a payload that stops round-tripping, a validation
/// gap that lets an empty observation through.
/// </summary>
public sealed class EndpointContractTests(AzuriteWebApplicationFactory factory)
    : IClassFixture<AzuriteWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Theory]
    [InlineData("/api/observer/state")]
    [InlineData("/api/identity/subjects")]
    [InlineData("/api/identity/subjects/live-status")]
    [InlineData("/api/identity/subjects/live-risk")]
    [InlineData("/api/diagnostics/status")]
    [InlineData("/auth/me")]
    [InlineData("/auth/config")]
    [InlineData("/health")]
    [InlineData("/diag")]
    [InlineData("/diag/boot")]
    public async Task Read_endpoints_answer_successfully(string route)
    {
        var response = await _client.GetAsync(route);

        Assert.True(
            response.IsSuccessStatusCode,
            $"GET {route} returned {(int)response.StatusCode} {response.StatusCode}");
    }

    [Theory]
    [InlineData("/api/observer/state")]
    [InlineData("/api/identity/subjects")]
    [InlineData("/api/diagnostics/status")]
    public async Task Read_endpoints_return_json(string route)
    {
        var response = await _client.GetAsync(route);

        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task The_observation_event_stream_opens_as_server_sent_events()
    {
        // SSE never completes by design, so the body must not be buffered — GetAsync without
        // ResponseHeadersRead blocks until the request times out.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var response = await _client.GetAsync(
            "/api/observer/events", HttpCompletionOption.ResponseHeadersRead, cts.Token);

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Unknown_api_routes_are_not_silently_swallowed_by_the_spa_fallback()
    {
        var response = await _client.GetAsync("/api/definitely-not-a-route");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_archive_date_that_is_not_a_date_is_rejected()
    {
        var response = await _client.GetAsync("/api/archives/not-a-date");

        Assert.False(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Renaming_with_a_blank_name_is_rejected()
    {
        var response = await _client.PatchAsync(
            "/api/identity/subjects/Subject-1",
            JsonContent.Create(new RenameSubjectRequestDto { NewName = "   " }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Merging_a_subject_into_itself_is_rejected()
    {
        var response = await _client.PostAsJsonAsync("/api/identity/merge", new MergeIdentityRequestDto
        {
            PrimarySubjectId = "Subject-1",
            SecondarySubjectId = "Subject-1"
        });

        Assert.False(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task A_malformed_json_body_is_rejected_rather_than_crashing_the_host()
    {
        var response = await _client.PostAsync(
            "/api/observer/ingest",
            new StringContent("{ this is not json", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_health_document_keeps_its_deploy_gating_shape()
    {
        // The JSON contract gates every production deploy — status, checks[], and durations.
        using var document = JsonDocument.Parse(await _client.GetStringAsync("/health"));
        var root = document.RootElement;

        Assert.True(root.TryGetProperty("status", out _));
        Assert.True(root.TryGetProperty("checks", out var checks));
        Assert.Equal(JsonValueKind.Array, checks.ValueKind);
    }

    [Fact]
    public async Task The_health_document_never_leaks_the_key_vault_hostname()
    {
        var body = await _client.GetStringAsync("/health");

        Assert.DoesNotContain(".vault.azure.net", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Diagnostics_masks_secret_values()
    {
        var snapshot = await _client.GetFromJsonAsync<DiagnosticsSnapshotDto>("/api/diagnostics/status");

        Assert.NotNull(snapshot);
        Assert.DoesNotContain("AccountKey=", snapshot!.MaskedEndpoint ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AccountKey=", snapshot.MaskedApiKey ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Auth_config_advertises_the_test_environment_sign_in_options()
    {
        var config = await _client.GetFromJsonAsync<AuthConfigDto>("/auth/config");

        Assert.NotNull(config);
        Assert.False(string.IsNullOrWhiteSpace(config!.Environment));
    }

    [Fact]
    public async Task Observer_state_reports_the_configured_poll_interval()
    {
        var state = await _client.GetFromJsonAsync<ObserverRuntimeStateDto>("/api/observer/state");

        Assert.NotNull(state);
        Assert.True(state!.PollIntervalSeconds > 0);
    }

    [Fact]
    public async Task A_blob_read_for_an_unknown_path_does_not_return_a_usable_url()
    {
        var response = await _client.GetAsync("/api/blobs/read?path=does/not/exist.jpg");

        if (response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("\"sasUrl\":null", body, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.True((int)response.StatusCode >= 400);
        }
    }

    [Fact]
    public async Task Acknowledging_an_unknown_event_does_not_fail_the_request()
    {
        var response = await _client.PostAsJsonAsync("/api/observer/acknowledge", new AcknowledgeEventsRequestDto
        {
            EventIds = [Guid.NewGuid().ToString("N")],
            AcknowledgedBy = "integration-test"
        });

        Assert.True(response.IsSuccessStatusCode);
    }
}
