using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using PoWatch.Shared.Models;

namespace PoWatch.IntegrationTests;

public sealed class FhirApiTests : IClassFixture<AzuriteWebApplicationFactory>
{
    private readonly AzuriteWebApplicationFactory _factory;

    public FhirApiTests(AzuriteWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task FhirObservationSearch_Returns503_WhenFeatureFlagDisabled()
    {
        using var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["FeatureFlags:FhirExportEnabled"] = "false"
                });
            });
        }).CreateClient();

        var response = await client.GetAsync($"/fhir/Observation?subject=Kim&date={DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task FhirObservationSearch_ReturnsFhirBundle_WhenFeatureFlagEnabled()
    {
        using var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["FeatureFlags:FhirExportEnabled"] = "true"
                });
            });
        }).CreateClient();

        var ingestResponse = await client.PostAsJsonAsync("/api/observer/ingest", new IngestObservationRequestDto
        {
            SubjectHint = "Kim",
            Activity = "Desk Work",
            ClinicalPayload = "<S>Known subject entered and resumed desk work.<E>",
            IsSignificant = true,
            SignificantReason = "Known subject change"
        });

        Assert.Equal(HttpStatusCode.OK, ingestResponse.StatusCode);

        var date = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var bundle = await client.GetFromJsonAsync<FhirBundleResponse>($"/fhir/Observation?subject=kim&date={date}&count=10");

        Assert.NotNull(bundle);
        Assert.Equal("Bundle", bundle.ResourceType);
        Assert.Equal("searchset", bundle.Type);
        Assert.True(bundle.Total >= 1);
        Assert.NotEmpty(bundle.Entry);
        Assert.All(bundle.Entry, entry => Assert.Equal("Observation", entry.Resource.ResourceType));
        Assert.Contains(bundle.Entry, entry =>
            entry.Resource.Subject.Reference.StartsWith("Patient/", StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(entry.Resource.Subject.Display));
    }

    private sealed class FhirBundleResponse
    {
        public string ResourceType { get; init; } = string.Empty;
        public string Type { get; init; } = string.Empty;
        public int Total { get; init; }
        public List<FhirBundleEntryResponse> Entry { get; init; } = [];
    }

    private sealed class FhirBundleEntryResponse
    {
        public string FullUrl { get; init; } = string.Empty;
        public FhirObservationResponse Resource { get; init; } = new();
    }

    private sealed class FhirObservationResponse
    {
        public string ResourceType { get; init; } = string.Empty;
        public string Id { get; init; } = string.Empty;
        public FhirSubjectResponse Subject { get; init; } = new();
    }

    private sealed class FhirSubjectResponse
    {
        public string Reference { get; init; } = string.Empty;
        public string Display { get; init; } = string.Empty;
    }
}