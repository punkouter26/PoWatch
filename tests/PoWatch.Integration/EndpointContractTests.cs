using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using PoWatch.Shared.Models;

namespace PoWatch.Integration;

/// <summary>
/// Shape-and-status coverage for the endpoints the client depends on. These are the contracts that
/// break silently: a route that starts 404-ing, a payload that stops round-tripping, a validation
/// gap that lets a malformed body through.
/// </summary>
public sealed class EndpointContractTests(AzuriteWebApplicationFactory factory)
    : IClassFixture<AzuriteWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Read_endpoints_answer_successfully()
    {
        foreach (string route in new string[] { "/api/sessions", "/api/regulars", "/api/achievements", "/api/stats/presence?range=today", "/api/diagnostics/status", "/auth/me", "/auth/config", "/health", "/diag", "/diag/boot" })
        {
            var response = await _client.GetAsync(route);

            Assert.True(
                response.IsSuccessStatusCode,
                $"GET {route} returned {(int)response.StatusCode} {response.StatusCode}");

        }
    }

    [Fact]
    public async Task Read_endpoints_return_json()
    {
        foreach (string route in new string[] { "/api/sessions", "/api/achievements", "/api/diagnostics/status" })
        {
            var response = await _client.GetAsync(route);

            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        }
    }

    [Fact]
    public async Task Unknown_api_routes_are_not_silently_swallowed_by_the_spa_fallback()
    {
        var response = await _client.GetAsync("/api/definitely-not-a-route");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_malformed_json_body_is_rejected_rather_than_crashing_the_host()
    {
        var response = await _client.PostAsync(
            "/api/sessions",
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
    public async Task The_retired_caregiver_endpoints_are_gone()
    {
        foreach (var route in new[] { "/api/observer/state", "/api/archives/2026-01-01", "/api/identity/subjects", "/api/blobs/read?blobPath=x.jpg" })
            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(route)).StatusCode);
    }
}
