using System.ClientModel;
using System.ClientModel.Primitives;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using PoWatch.Application.Options;
using PoWatch.Application.Contracts;
using PoWatch.Shared.Models;

namespace PoWatch.Infrastructure.Runtime;

/// <summary>
/// Registers the configured <see cref="IChatClient"/> pipelines, defaulting to
/// <c>AiProvider:Provider</c>. Template (the default) uses no client, so recaps stay fully
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
        services.AddSingleton<IAiModelRegistry>(sp => new ModelRegistry(configuration, sp));
        return services;
    }

    /// <summary>How many model turns one request may spend calling tools.</summary>
    public const int MaxToolRounds = 4;

    private sealed class ModelRegistry : IAiModelRegistry, IDisposable
    {
        private readonly Dictionary<string, IChatClient> _clients = new(StringComparer.Ordinal);

        public ModelRegistry(IConfiguration configuration, IServiceProvider services)
        {
            var models = new List<AiModelDto> { new("Template", "Template", "No server AI") };
            foreach (var provider in new[] { AiProviderType.AzureOpenAi, AiProviderType.Gemini, AiProviderType.Ollama })
            {
                if (Client(provider, configuration) is not { } client) continue;
                var id = provider.ToString();
                var model = provider switch
                {
                    AiProviderType.AzureOpenAi => configuration["AzureOpenAi:DeploymentName"] ?? new AzureOpenAiOptions().DeploymentName,
                    AiProviderType.Gemini => configuration["Gemini:Model"] ?? new GeminiOptions().Model,
                    _ => configuration["Ollama:Model"] ?? new OllamaOptions().Model
                };
                _clients.Add(id, new ChatClientBuilder(client)
                    .UseFunctionInvocation(configure: f => f.MaximumIterationsPerRequest = MaxToolRounds)
                    .UseDistributedCache(configure: cache => cache.CacheKeyAdditionalValues = [id, model])
                    .Build(services));
                models.Add(new AiModelDto(id, provider == AiProviderType.AzureOpenAi ? "Azure OpenAI" : id, model));
            }
            Models = models.AsReadOnly();
            var preferred = configuration.GetSection("AiProvider").Get<AiProviderOptions>()?.Provider.ToString() ?? "Template";
            DefaultId = Models.Any(m => m.Id == preferred) ? preferred : "Template";
        }

        public IReadOnlyList<AiModelDto> Models { get; }
        public string DefaultId { get; }

        public IChatClient? GetClient(string id) => id == "Template" ? null : _clients[id];

        public void Dispose()
        {
            foreach (var client in _clients.Values) client.Dispose();
        }
    }

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
