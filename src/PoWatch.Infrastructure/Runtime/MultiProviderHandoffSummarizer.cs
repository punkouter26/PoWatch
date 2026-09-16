using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PoWatch.Application.Contracts;
using PoWatch.Application.Options;
using PoWatch.Shared.Models;

namespace PoWatch.Infrastructure.Runtime;

/// <summary>
/// Unified multi-provider summarizer for Handoff Coach.
/// Intelligently routes generation requests between Azure OpenAI, local facility Ollama instances,
/// or deterministic Template summarization based on configuration and runtime availability.
/// </summary>
public sealed class MultiProviderHandoffSummarizer(
    TemplateHandoffSummarizer templateFallback,
    AzureOpenAiHandoffSummarizer azureOpenAiSummarizer,
    HttpClient http,
    IOptions<AiProviderOptions> aiOptions,
    ILogger<MultiProviderHandoffSummarizer> logger) : IHandoffSummarizer
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<HandoffSummaryContent> SummarizeAsync(HandoffSummarizerContext context, CancellationToken cancellationToken)
    {
        var opts = aiOptions.Value;

        // Route to Ollama if configured and enabled
        if (opts.Provider == AiProviderType.Ollama)
        {
            return await SummarizeWithOllamaAsync(context, opts, cancellationToken);
        }

        // Route to Azure OpenAI if enabled
        if (opts.Provider == AiProviderType.AzureOpenAi)
        {
            return await azureOpenAiSummarizer.SummarizeAsync(context, cancellationToken);
        }

        // Default to deterministic template
        return await templateFallback.SummarizeAsync(context, cancellationToken);
    }

    private async Task<HandoffSummaryContent> SummarizeWithOllamaAsync(
        HandoffSummarizerContext context,
        AiProviderOptions opts,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(opts.OllamaEndpoint))
        {
            logger.LogWarning("Ollama summarizer selected but OllamaEndpoint is empty. Falling back to template.");
            return await templateFallback.SummarizeAsync(context, cancellationToken);
        }

        try
        {
            var systemPrompt =
                """
                You are a clinical handoff documentation assistant for PoWatch.
                Generate a concise, accurate SBAR (Situation, Background, Assessment, Recommendation) shift handoff brief based ONLY on grounded observation data provided.
                Do NOT invent information not present in the data.
                Always respond with valid JSON matching this exact schema:
                {"summary":"string","priority_items":["string"],"follow_ups":["string"],"source_notes":["string"]}
                """;

            var userPrompt =
                $"""
                Generate a {context.Audience} handoff brief for the {context.ShiftWindow} shift on {context.Report.Date:yyyy-MM-dd}.
                Total events: {context.Report.TotalEvents}.
                Primary subject: {context.Report.PrimarySubject}.
                Dominant activity: {context.Report.DominantActivity}.
                Clinical narrative: {context.Report.ClinicalNarrative}.
                Outlier count: {context.Report.OutlierCount}.
                Significant count: {context.Report.SignificantCount}.
                Respond ONLY with valid JSON.
                """;

            var requestBody = new
            {
                model = opts.OllamaModel,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                stream = false,
                format = "json",
                options = new
                {
                    temperature = opts.Temperature,
                    num_predict = opts.MaxTokens
                }
            };

            var url = $"{opts.OllamaEndpoint.TrimEnd('/')}/api/chat";
            var json = JsonSerializer.Serialize(requestBody);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            logger.LogDebug("Sending handoff brief request to Ollama. Endpoint={Endpoint} Model={Model}", opts.OllamaEndpoint, opts.OllamaModel);

            using var response = await http.SendAsync(httpRequest, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Ollama returned status {StatusCode}. Falling back to template.", response.StatusCode);
                return await templateFallback.SummarizeAsync(context, cancellationToken);
            }

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var chatResponse = JsonSerializer.Deserialize<OllamaChatResponse>(responseJson, _jsonOptions);
            var rawContent = chatResponse?.Message?.Content;

            if (string.IsNullOrWhiteSpace(rawContent))
            {
                logger.LogWarning("Ollama returned empty response. Falling back to template.");
                return await templateFallback.SummarizeAsync(context, cancellationToken);
            }

            var parsed = ParseStructuredOutput(rawContent, opts.OllamaModel);
            if (parsed is null)
            {
                logger.LogWarning("Ollama output could not be parsed as JSON. Falling back to template. Raw={Raw}", rawContent);
                return await templateFallback.SummarizeAsync(context, cancellationToken);
            }

            logger.LogInformation("Ollama handoff brief generated successfully. Model={Model}", opts.OllamaModel);
            return parsed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Ollama handoff brief generation failed. Falling back to template.");
            return await templateFallback.SummarizeAsync(context, cancellationToken);
        }
    }

    private static HandoffSummaryContent? ParseStructuredOutput(string raw, string modelName)
    {
        var cleaned = raw.Trim();
        if (cleaned.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = cleaned.IndexOf('\n');
            var lastFence = cleaned.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline >= 0 && lastFence > firstNewline)
                cleaned = cleaned[(firstNewline + 1)..lastFence].Trim();
        }

        try
        {
            using var doc = JsonDocument.Parse(cleaned);
            var root = doc.RootElement;

            var summary = root.TryGetProperty("summary", out var s) ? s.GetString() ?? string.Empty : string.Empty;
            var priority = root.TryGetProperty("priority_items", out var pi) ? ReadStringArray(pi) : [];
            var followUps = root.TryGetProperty("follow_ups", out var fu) ? ReadStringArray(fu) : [];
            var sourceNotes = root.TryGetProperty("source_notes", out var sn)
                ? ReadStringArray(sn)
                : new List<string> { $"Generated on local facility gateway via Ollama ({modelName})." };

            if (string.IsNullOrWhiteSpace(summary)) return null;

            return new HandoffSummaryContent
            {
                Summary = summary,
                PriorityItems = priority,
                FollowUps = followUps,
                SourceNotes = sourceNotes,
                IsAiGenerated = true
            };
        }
        catch
        {
            return null;
        }
    }

    private static List<string> ReadStringArray(JsonElement element)
    {
        var result = new List<string>();
        if (element.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in element.EnumerateArray())
        {
            var val = item.GetString();
            if (!string.IsNullOrWhiteSpace(val))
                result.Add(val);
        }
        return result;
    }

    private sealed class OllamaChatResponse
    {
        [JsonPropertyName("message")]
        public OllamaMessage? Message { get; set; }
    }

    private sealed class OllamaMessage
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}

