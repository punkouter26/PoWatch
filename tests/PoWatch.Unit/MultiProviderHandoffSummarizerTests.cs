using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PoWatch.Application.Contracts;
using PoWatch.Application.Options;
using PoWatch.Infrastructure.Runtime;
using PoWatch.Shared.Models;

namespace PoWatch.Unit;

public sealed class MultiProviderHandoffSummarizerTests
{
    [Fact]
    public async Task SummarizeAsync_RoutesToTemplate_WhenAllAiDisabled()
    {
        var template = new TemplateHandoffSummarizer();
        var azureOpenAi = BuildAzureSummarizer(new TestHandler(HttpStatusCode.OK, "{}"));
        var summarizer = new MultiProviderHandoffSummarizer(
            template,
            azureOpenAi,
            new HttpClient(new TestHandler(HttpStatusCode.OK, "{}")),
            Options.Create(new AiProviderOptions { Provider = AiProviderType.Template }),
            NullLogger<MultiProviderHandoffSummarizer>.Instance);

        var context = BuildContext();
        var result = await summarizer.SummarizeAsync(context, CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(result.IsAiGenerated);
        Assert.Contains("Nothing was flagged for review", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SummarizeAsync_RoutesToOllama_WhenOllamaSelected()
    {
        const string ollamaResponseJson =
            """
            {
              "message": {
                "content": "{\"summary\":\"SBAR: Patient resting peacefully throughout shift.\",\"priority_items\":[\"Monitor blood pressure\"],\"follow_ups\":[\"Check morning vitals\"],\"source_notes\":[\"Grounded in PoWatch data\"]}"
              }
            }
            """;

        var template = new TemplateHandoffSummarizer();
        var azureOpenAi = BuildAzureSummarizer(new TestHandler(HttpStatusCode.OK, "{}"));
        var handler = new TestHandler(HttpStatusCode.OK, ollamaResponseJson);
        var http = new HttpClient(handler);

        var summarizer = new MultiProviderHandoffSummarizer(
            template,
            azureOpenAi,
            http,
            Options.Create(new AiProviderOptions
            {
                Provider = AiProviderType.Ollama,
                OllamaEndpoint = "http://localhost:11434",
                OllamaModel = "llama3.2-vision"
            }),
            NullLogger<MultiProviderHandoffSummarizer>.Instance);

        var context = BuildContext();
        var result = await summarizer.SummarizeAsync(context, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.IsAiGenerated);
        Assert.Contains("SBAR: Patient resting peacefully", result.Summary, StringComparison.Ordinal);
        Assert.Single(result.PriorityItems);
        Assert.Equal("Monitor blood pressure", result.PriorityItems[0]);
    }

    [Fact]
    public async Task SummarizeAsync_FallsBackToTemplate_WhenOllamaFails()
    {
        var template = new TemplateHandoffSummarizer();
        var azureOpenAi = BuildAzureSummarizer(new TestHandler(HttpStatusCode.OK, "{}"));
        var handler = new TestHandler(HttpStatusCode.InternalServerError, "Service Unavailable");
        var http = new HttpClient(handler);

        var summarizer = new MultiProviderHandoffSummarizer(
            template,
            azureOpenAi,
            http,
            Options.Create(new AiProviderOptions
            {
                Provider = AiProviderType.Ollama,
                OllamaEndpoint = "http://localhost:11434"
            }),
            NullLogger<MultiProviderHandoffSummarizer>.Instance);

        var context = BuildContext();
        var result = await summarizer.SummarizeAsync(context, CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(result.IsAiGenerated);
    }

    private static AzureOpenAiHandoffSummarizer BuildAzureSummarizer(HttpMessageHandler handler)
    {
        return new AzureOpenAiHandoffSummarizer(
            new TemplateHandoffSummarizer(),
            new HttpClient(handler),
            Options.Create(new AzureOpenAiOptions
            {
                Endpoint = "https://mock.openai.azure.com/",
                ApiKey = "mock-key",
                DeploymentName = "gpt-5.4-nano"
            }),
            NullLogger<AzureOpenAiHandoffSummarizer>.Instance);
    }

    private static HandoffSummarizerContext BuildContext() => new()
    {
        ShiftWindow = nameof(ShiftWindow.FullDay),
        Audience = "NurseToNurse",
        DriftStatus = [],
        Report = new ShiftHandoffReportDto
        {
            Date = DateOnly.FromDateTime(DateTime.UtcNow),
            ShiftWindow = ShiftWindow.FullDay,
            WindowStartUtc = DateTimeOffset.UtcNow.AddHours(-8),
            WindowEndUtc = DateTimeOffset.UtcNow,
            PrimarySubject = "Person 1",
            DominantActivity = "Resting",
            TotalEvents = 10,
            OutlierCount = 0,
            SignificantCount = 0,
            OutlierEvents = [],
            SignificantEvents = []
        }
    };

    private sealed class TestHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
