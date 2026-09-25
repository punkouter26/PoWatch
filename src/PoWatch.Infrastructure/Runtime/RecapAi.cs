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
/// deterministic and offline. Azure OpenAI is reached through its OpenAI-compatible v1 endpoint, with
/// an API key or, when none is set, the app's Entra identity (managed identity in App Service, the
/// az CLI login locally). Responses are cached by prompt, so the same facts never cost twice.
/// </summary>
public static class RecapAi
{
    public static IServiceCollection AddPoWatchRecapAi(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var ai = configuration.GetSection("AiProvider").Get<AiProviderOptions>() ?? new AiProviderOptions();
        var azure = configuration.GetSection("AzureOpenAi").Get<AzureOpenAiOptions>() ?? new AzureOpenAiOptions();

        if (ai.Provider != AiProviderType.AzureOpenAi || !Uri.TryCreate(azure.Endpoint, UriKind.Absolute, out var endpoint))
            return services;
        var client = OpenAi(new Uri(endpoint, "openai/v1/"), azure.ApiKey, azure.DeploymentName);

        // Resolves the host's IDistributedCache (Program.cs registers the in-memory one).
        services.AddChatClient(client).UseDistributedCache();
        return services;
    }

    private static IChatClient OpenAi(Uri endpoint, string apiKey, string model)
    {
        var options = new OpenAIClientOptions { Endpoint = endpoint };
        // The token-policy constructor is marked evaluation-only; it is the SDK's documented keyless
        // route to Azure's v1 endpoint, and the alternative is a second SDK (Azure.AI.OpenAI).
#pragma warning disable OPENAI001
        var client = string.IsNullOrWhiteSpace(apiKey)
            ? new OpenAIClient(new BearerTokenPolicy(new DefaultAzureCredential(), "https://cognitiveservices.azure.com/.default"), options)
            : new OpenAIClient(new ApiKeyCredential(apiKey), options);
#pragma warning restore OPENAI001
        return client.GetChatClient(model).AsIChatClient();
    }
}
