using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OllamaSharp;
using OpenAI;
using PoWatch.Application.Options;

namespace PoWatch.Infrastructure.Runtime;

/// <summary>
/// Registers the one optional <see cref="IChatClient"/> recaps may use, chosen by
/// <c>AiProvider:Provider</c>. Template (the default) registers nothing, so recaps stay fully
/// deterministic and offline. Azure OpenAI is reached through its OpenAI-compatible v1 endpoint.
/// </summary>
public static class RecapAi
{
    public static IServiceCollection AddPoWatchRecapAi(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var ai = configuration.GetSection("AiProvider").Get<AiProviderOptions>() ?? new AiProviderOptions();
        var azure = configuration.GetSection("AzureOpenAi").Get<AzureOpenAiOptions>() ?? new AzureOpenAiOptions();

        switch (ai.Provider)
        {
            case AiProviderType.Ollama when Uri.TryCreate(ai.OllamaEndpoint, UriKind.Absolute, out var ollama):
                services.AddChatClient(_ => new OllamaApiClient(ollama, ai.OllamaModel));
                break;

            case AiProviderType.AzureOpenAi when Uri.TryCreate(azure.Endpoint, UriKind.Absolute, out var endpoint) && !string.IsNullOrWhiteSpace(azure.ApiKey):
                services.AddChatClient(_ => new OpenAIClient(
                        new ApiKeyCredential(azure.ApiKey),
                        new OpenAIClientOptions { Endpoint = new Uri(endpoint, "openai/v1/") })
                    .GetChatClient(azure.DeploymentName)
                    .AsIChatClient());
                break;
        }

        return services;
    }
}
