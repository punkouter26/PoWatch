using System.ComponentModel.DataAnnotations;

namespace PoWatch.Application.Options;

/// <summary>Supported AI backends for clinical synthesis and handoff brief generation.</summary>
public enum AiProviderType
{
    /// <summary>Azure OpenAI Service deployment (default cloud provider).</summary>
    AzureOpenAi,

    /// <summary>Local facility edge gateway running Ollama (zero-cloud, HIPAA-isolated on-premise).</summary>
    Ollama,

    /// <summary>Deterministic template-based synthesis without external LLM calls.</summary>
    Template
}

/// <summary>Configures the unified AI provider used by Handoff Coach and server-side synthesis.</summary>
public sealed class AiProviderOptions
{
    /// <summary>Selected AI provider. Defaults to Template; external providers fall back to Template on failure.</summary>
    public AiProviderType Provider { get; init; } = AiProviderType.Template;

    /// <summary>Base URI for local or facility-hosted Ollama server.</summary>
    public string OllamaEndpoint { get; init; } = "http://localhost:11434";

    /// <summary>Model identifier to query on the Ollama runtime, e.g. llama3.2-vision, minicpm-v, or qwen2.5-coder.</summary>
    [Required]
    public string OllamaModel { get; init; } = "llama3.2-vision";

    /// <summary>Sampling temperature (0–2).</summary>
    [Range(0.0, 2.0)]
    public double Temperature { get; init; } = 0.3;

    /// <summary>Maximum tokens to request in the generation.</summary>
    [Range(1, 128_000)]
    public int MaxTokens { get; init; } = 600;
}
