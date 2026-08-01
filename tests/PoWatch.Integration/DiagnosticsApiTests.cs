using System.Net;
using System.Net.Http.Json;
using PoWatch.Domain.Models;

namespace PoWatch.Integration;

public sealed class DiagnosticsApiTests : IClassFixture<AzuriteWebApplicationFactory>
{
    private readonly HttpClient _client;

    public DiagnosticsApiTests(AzuriteWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task DiagnosticsEndpoint_ReturnsMaskedAndHealthySnapshot()
    {
        var response = await _client.GetAsync("/api/diagnostics/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var snapshot = await response.Content.ReadFromJsonAsync<DiagnosticsSnapshot>();
        Assert.NotNull(snapshot);
        Assert.Equal("Azurite-OK", snapshot.StorageConnectionStatus);
        Assert.Contains("...", snapshot.MaskedEndpoint);
        Assert.Contains("...", snapshot.MaskedApiKey);
        Assert.DoesNotContain("DEV-LOCAL-KEY-12345", snapshot.MaskedApiKey, StringComparison.OrdinalIgnoreCase);
    }
}
