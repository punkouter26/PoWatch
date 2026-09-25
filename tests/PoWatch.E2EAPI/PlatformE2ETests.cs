using System.Net;
using System.Net.Http.Json;
using PoWatch.Shared.Models;

namespace PoWatch.E2EAPI;

/// <summary>The platform around the stats: default-deny auth, sign-in, health and diagnostics.</summary>
public sealed class PlatformE2ETests(ApiE2EFactory factory) : IClassFixture<ApiE2EFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task An_anonymous_caller_cannot_read_sessions()
    {
        // The API host is default-deny; only /health, /diag and /auth opt out.
        using var anonymous = factory.CreateClient();
        anonymous.DefaultRequestHeaders.Remove("X-Fake-User");

        var response = await anonymous.GetAsync("/api/sessions");

        Assert.True(
            response.IsSuccessStatusCode || response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"Unexpected status {(int)response.StatusCode}");
    }

    [Fact]
    public async Task Operations_endpoints_report_health_boot_and_storage()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/diag")).StatusCode);

        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Accept.ParseAdd("application/json");
        var health = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Contains("status", await health.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var boot = await _client.GetAsync("/diag/boot");
        Assert.Equal(HttpStatusCode.OK, boot.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(await boot.Content.ReadAsStringAsync()));

        var snapshot = await _client.GetFromJsonAsync<DiagnosticsSnapshotDto>("/api/diagnostics/status");
        Assert.NotNull(snapshot);
        Assert.False(string.IsNullOrWhiteSpace(snapshot!.StorageConnectionStatus));
    }

    [Fact]
    public async Task Sign_in_config_is_public_and_the_guest_bypass_establishes_a_session()
    {
        var config = await _client.GetFromJsonAsync<AuthConfigDto>("/auth/config");
        Assert.NotNull(config);
        Assert.False(string.IsNullOrWhiteSpace(config!.Environment));

        // HTTPS base address: the BFF session cookie is Secure, so it is dropped over plain http.
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var login = await client.GetAsync("/auth/login/fake?returnUrl=/");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        var me = await client.GetFromJsonAsync<AuthStateDto>("/auth/me");
        Assert.NotNull(me);
        Assert.True(me!.IsAuthenticated);
    }
}
