using System.Net;
using System.Net.Http.Json;
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

    [Fact]
    public async Task BlobReadEndpoint_ReturnsReadSasDescriptor()
    {
        // First, get an upload access to create a blob path
        var uploadResponse = await _client.GetAsync("/api/blobs/sas?subjectId=Subject-5&date=20260415");
        Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);
        
        var uploadDescriptor = await uploadResponse.Content.ReadFromJsonAsync<BlobSasResponse>();
        Assert.NotNull(uploadDescriptor);

        // Now request read access for that blob path
        var readResponse = await _client.GetAsync($"/api/blobs/read?blobPath={Uri.EscapeDataString(uploadDescriptor.BlobPath)}");
        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);

        var readDescriptor = await readResponse.Content.ReadFromJsonAsync<BlobSasResponse>();
        Assert.NotNull(readDescriptor);
        Assert.NotNull(readDescriptor.SasUrl);
        // Read SAS should include query parameters
        Assert.Contains("?", readDescriptor.SasUrl);
    }

    [Fact]
    public async Task BlobReadEndpoint_ReturnsBadRequest_WhenBlobPathMissing()
    {
        var response = await _client.GetAsync("/api/blobs/read");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BlobIntegrityEndpoint_ChecksMultipleBlobs()
    {
        var blobPaths = new[]
        {
            "significant-images/20260416/Subject-10/test-image-1.svg",
            "significant-images/20260416/Subject-10/test-image-2.svg"
        };

        var response = await _client.PostAsJsonAsync("/api/blobs/integrity", blobPaths);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var integrityResult = await response.Content.ReadFromJsonAsync<dynamic>();
        Assert.NotNull(integrityResult);
    }

    [Fact]
    public async Task BlobIntegrityEndpoint_ReturnsBadRequest_WhenNoBlobPaths()
    {
        var response = await _client.PostAsJsonAsync("/api/blobs/integrity", Array.Empty<string>());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed class BlobSasResponse
    {
        public string SasUrl { get; init; } = string.Empty;
        public string BlobPath { get; init; } = string.Empty;
        public DateTimeOffset ExpiresAtUtc { get; init; }
    }
}
