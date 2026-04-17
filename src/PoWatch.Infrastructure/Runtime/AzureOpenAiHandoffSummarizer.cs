using System.Net.Http.Headers;
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
/// Azure OpenAI-powered handoff brief generator.
/// Calls the Azure OpenAI Chat Completions REST API with a grounded prompt built exclusively
/// from PoWatch observation data. Falls back to <see cref="TemplateHandoffSummarizer"/>
/// on any configuration or connectivity error so the feature degrades gracefully.
/// </summary>
public sealed class AzureOpenAiHandoffSummarizer(
    TemplateHandoffSummarizer templateFallback,
    IOptions<AzureOpenAiOptions> openAiOptions,
    ILogger<AzureOpenAiHandoffSummarizer> logger) : IHandoffSummarizer
{
    // Shared static HttpClient — safe for high-concurrency singleton usage
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(45) };

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<HandoffSummaryContent> SummarizeAsync(HandoffSummarizerContext context, CancellationToken cancellationToken)
    {
        var opts = openAiOptions.Value;

        if (string.IsNullOrWhiteSpace(opts.Endpoint) || string.IsNullOrWhiteSpace(opts.ApiKey))
        {
            logger.LogWarning("Azure OpenAI Handoff Summarizer: endpoint or key not configured. Falling back to template.");
            return await templateFallback.SummarizeAsync(context, cancellationToken);
        }

        try
        {
            var systemPrompt = BuildSystemPrompt();
            var userPrompt = BuildUserPrompt(context);

            var requestBody = new
            {
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                max_tokens = opts.MaxCompletionTokens,
                temperature = opts.Temperature
            };

            var url = $"{opts.Endpoint.TrimEnd('/')}/openai/deployments/{opts.DeploymentName}/chat/completions?api-version={opts.ApiVersion}";
            var json = JsonSerializer.Serialize(requestBody);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            httpRequest.Headers.Add("api-key", opts.ApiKey);

            logger.LogDebug("Azure OpenAI handoff brief request. Deployment={Deployment} Tokens={Tokens}", opts.DeploymentName, opts.MaxCompletionTokens);

            using var response = await _http.SendAsync(httpRequest, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogError(
                    "Azure OpenAI returned non-success. Status={Status} Body={Body}. Falling back to template.",
                    response.StatusCode, errorBody);
                return await templateFallback.SummarizeAsync(context, cancellationToken);
            }

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var aiResponse = JsonSerializer.Deserialize<AzureOpenAiChatResponse>(responseJson, _jsonOptions);
            var rawContent = aiResponse?.Choices?.FirstOrDefault()?.Message?.Content;

            if (string.IsNullOrWhiteSpace(rawContent))
            {
                logger.LogWarning("Azure OpenAI returned empty content. Falling back to template.");
                return await templateFallback.SummarizeAsync(context, cancellationToken);
            }

            var parsed = ParseStructuredOutput(rawContent);
            if (parsed is null)
            {
                logger.LogWarning("Azure OpenAI response could not be parsed as structured JSON. Falling back to template. RawContent={Raw}", rawContent);
                return await templateFallback.SummarizeAsync(context, cancellationToken);
            }

            logger.LogInformation("Azure OpenAI handoff brief generated successfully. PriorityItems={Count}", parsed.PriorityItems.Count);

            return parsed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Azure OpenAI handoff brief generation failed. Falling back to template.");
            return await templateFallback.SummarizeAsync(context, cancellationToken);
        }
    }

    private static string BuildSystemPrompt() =>
        """
        You are a clinical handoff documentation assistant for PoWatch, a room observation monitoring system.
        Generate concise, accurate handoff briefs based ONLY on the observation data provided.
        Do NOT invent, assume, or add information not present in the data.
        Always respond with valid JSON matching this exact schema:
        {"summary":"string","priority_items":["string"],"follow_ups":["string"],"source_notes":["string"]}
        """;

    private static string BuildUserPrompt(HandoffSummarizerContext context)
    {
        var r = context.Report;
        var sb = new StringBuilder();

        sb.AppendLine($"Generate a {context.Audience} handoff brief for the {context.ShiftWindow} shift on {r.Date:yyyy-MM-dd}.");
        sb.AppendLine();
        sb.AppendLine("SHIFT DATA:");
        sb.AppendLine($"- Total events: {r.TotalEvents}");
        sb.AppendLine($"- Primary subject: {r.PrimarySubject}");
        sb.AppendLine($"- Dominant activity: {r.DominantActivity}");
        sb.AppendLine($"- Outliers flagged: {r.OutlierCount}");
        sb.AppendLine($"- Significant events: {r.SignificantCount}");
        sb.AppendLine($"- Clinical narrative: {r.ClinicalNarrative}");

        if (context.IncludeHighlights && r.OutlierEvents.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("CLINICAL OUTLIERS (top 5):");
            foreach (var e in r.OutlierEvents.Take(5))
                sb.AppendLine($"  - {e.ObservedAtUtc:HH:mm} {e.SubjectDisplayName}: {e.Activity}");
        }

        if (context.IncludeHighlights && r.SignificantEvents.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("SIGNIFICANT EVENTS (top 5):");
            foreach (var e in r.SignificantEvents.Take(5))
                sb.AppendLine($"  - {e.ObservedAtUtc:HH:mm} {e.SubjectDisplayName}: {e.Activity}{(string.IsNullOrWhiteSpace(e.SignificantReason) ? string.Empty : $" ({e.SignificantReason})")}");
        }

        if (context.DriftStatus.Count > 0)
        {
            var notable = context.DriftStatus.Where(d => d.DriftLabel is not "Normal" and not "Insufficient Data").Take(3).ToList();
            if (notable.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("BEHAVIORAL DRIFT ALERTS:");
                foreach (var d in notable)
                    sb.AppendLine($"  - {d.DisplayName}: {d.DriftLabel} (score {d.DriftScore:F0}/100)");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Respond ONLY with the JSON object. Do not include markdown or explanation.");

        return sb.ToString();
    }

    private static HandoffSummaryContent? ParseStructuredOutput(string raw)
    {
        // Strip optional markdown code fences
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
            var doc = JsonDocument.Parse(cleaned);
            var root = doc.RootElement;

            var summary = root.TryGetProperty("summary", out var s) ? s.GetString() ?? string.Empty : string.Empty;
            var priority = root.TryGetProperty("priority_items", out var pi) ? ReadStringArray(pi) : [];
            var followUps = root.TryGetProperty("follow_ups", out var fu) ? ReadStringArray(fu) : [];
            var sourceNotes = root.TryGetProperty("source_notes", out var sn) ? ReadStringArray(sn) : new List<string> { "Generated by Azure OpenAI — grounded on PoWatch shift data." };

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

    // Minimal response model for deserializing Azure OpenAI chat completions
    private sealed class AzureOpenAiChatResponse
    {
        [JsonPropertyName("choices")]
        public List<ChatChoice>? Choices { get; set; }
    }

    private sealed class ChatChoice
    {
        [JsonPropertyName("message")]
        public ChatMessage? Message { get; set; }
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}
