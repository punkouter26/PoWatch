using System.ComponentModel.DataAnnotations;

namespace PoWatch.Application.Options;

/// <summary>Configures the Azure OpenAI connection used by the Handoff Coach feature.</summary>
public sealed class AzureOpenAiOptions
{
    /// <summary>Azure OpenAI resource endpoint, e.g. https://my-resource.openai.azure.com/</summary>
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>Azure OpenAI API key. In production, supply via Azure Key Vault.</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>Name of the deployed chat model, e.g. gpt-5.4-nano.</summary>
    [Required]
    public string DeploymentName { get; init; } = "gpt-5.4-nano";

    /// <summary>Azure OpenAI API version used in REST calls.</summary>
    [Required]
    public string ApiVersion { get; init; } = "2024-02-01";

    /// <summary>Maximum tokens to request in the completion.</summary>
    [Range(1, 128_000)]
    public int MaxCompletionTokens { get; init; } = 600;

    /// <summary>Sampling temperature (0–2). Lower values produce more deterministic output.</summary>
    [Range(0.0, 2.0)]
    public double Temperature { get; init; } = 0.3;
}
