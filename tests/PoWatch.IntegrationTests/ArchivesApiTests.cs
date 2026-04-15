using System.Net;
using System.Net.Http.Json;
using PoWatch.Application.Models;
using PoWatch.Domain.Models;

namespace PoWatch.IntegrationTests;

public sealed class ArchivesApiTests : IClassFixture<AzuriteWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ArchivesApiTests(AzuriteWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ArchivesEndpoint_ReturnsEmptyChapter_WhenNoDataExists()
    {
        var response = await _client.GetAsync("/api/archives/2026-04-01");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var chapter = await response.Content.ReadFromJsonAsync<DailyChapter>();
        Assert.NotNull(chapter);
        Assert.Empty(chapter.Timeline);
    }

    [Fact]
    public async Task BlobSasEndpoint_ReturnsUploadDescriptor()
    {
        var response = await _client.GetAsync("/api/blobs/sas?subjectId=Subject-4&date=20260414");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<BlobSasResponse>();
        Assert.NotNull(result);
        Assert.Contains("significant-images/20260414/Subject-4/", result.BlobPath);
        Assert.False(string.IsNullOrWhiteSpace(result.SasUrl));
    }

    private sealed class BlobSasResponse
    {
        public string SasUrl { get; init; } = string.Empty;
        public string BlobPath { get; init; } = string.Empty;
        public DateTimeOffset ExpiresAtUtc { get; init; }
    }
}
