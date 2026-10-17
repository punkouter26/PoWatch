using System.ClientModel;
using System.ClientModel.Primitives;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using PoWatch.Application.Options;

namespace PoWatch.Infrastructure.Runtime;

/// <summary>
/// Registers the one optional <see cref="IChatClient"/> recaps may use, chosen by
/// <c>AiProvider:Provider</c>. Template (the default) registers nothing, so recaps stay fully
/// deterministic and offline. Every provider is reached through an OpenAI-compatible endpoint, so one
/// SDK serves them all: Azure OpenAI's v1 endpoint, with an API key or, when none is set, the app's
/// Entra identity (managed identity in App Service, the az CLI login locally); Gemini's with an AI
/// Studio key; a local Ollama server with none. Responses are cached by prompt, so the same facts
/// never cost twice. The same client answers "Ask PoWatch", which is why it can call functions.
/// </summary>
public static class RecapAi
{
    public static IServiceCollection AddPoWatchRecapAi(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var ai = configuration.GetSection("AiProvider").Get<AiProviderOptions>() ?? new AiProviderOptions();
        if (Client(ai.Provider, configuration) is not { } client) return services;

        // Function calls run outside the cache: each model turn is cached by its full prompt, tool
        // results included, so a question is answered again as soon as the statistics behind it change.
        // The cache is the host's IDistributedCache (Program.cs registers the in-memory one). A question
        // gets a few tool rounds at most, so a model that keeps calling tools cannot run up the bill.
        services.AddChatClient(client)
            .UseFunctionInvocation(configure: f => f.MaximumIterationsPerRequest = MaxToolRounds)
            .UseDistributedCache();
        return services;
    }

    /// <summary>How many model turns one request may spend calling tools.</summary>
    public const int MaxToolRounds = 4;

    private static IChatClient? Client(AiProviderType provider, IConfiguration configuration)
    {
        switch (provider)
        {
            case AiProviderType.AzureOpenAi:
                var azure = configuration.GetSection("AzureOpenAi").Get<AzureOpenAiOptions>() ?? new AzureOpenAiOptions();
                return Uri.TryCreate(azure.Endpoint, UriKind.Absolute, out var endpoint)
                    ? OpenAi(new Uri(endpoint, "openai/v1/"), string.IsNullOrWhiteSpace(azure.ApiKey) ? null : new ApiKeyCredential(azure.ApiKey), azure.DeploymentName)
                    : null;
            case AiProviderType.Gemini:
                var gemini = configuration.GetSection("Gemini").Get<GeminiOptions>() ?? new GeminiOptions();
                return !string.IsNullOrWhiteSpace(gemini.ApiKey) && Uri.TryCreate(gemini.Endpoint, UriKind.Absolute, out var google)
                    ? OpenAi(google, new ApiKeyCredential(gemini.ApiKey), gemini.Model)
                    : null;
            case AiProviderType.Ollama:
                var ollama = configuration.GetSection("Ollama").Get<OllamaOptions>() ?? new OllamaOptions();
                // Ollama ignores the key, but the SDK will not send a request without one.
                return Uri.TryCreate(ollama.Endpoint, UriKind.Absolute, out var local)
                    ? OpenAi(local, new ApiKeyCredential("ollama"), ollama.Model)
                    : null;
            default:
                return null;
        }
    }

    /// <param name="key">Null signs in to Azure with the app's Entra identity instead.</param>
    private static IChatClient OpenAi(Uri endpoint, ApiKeyCredential? key, string model)
    {
        var options = new OpenAIClientOptions { Endpoint = endpoint };
        // The token-policy constructor is marked evaluation-only; it is the SDK's documented keyless
        // route to Azure's v1 endpoint, and the alternative is a second SDK (Azure.AI.OpenAI).
#pragma warning disable OPENAI001
        var client = key is null
            ? new OpenAIClient(new BearerTokenPolicy(new DefaultAzureCredential(), "https://cognitiveservices.azure.com/.default"), options)
            : new OpenAIClient(key, options);
#pragma warning restore OPENAI001
        return client.GetChatClient(model).AsIChatClient();
    }
}
