using System.ComponentModel.DataAnnotations;

namespace PoWatch.Application.Options;

/// <summary>Who writes the recap paragraph.</summary>
public enum AiProviderType
{
    /// <summary>Azure OpenAI Service deployment (default cloud provider).</summary>
    AzureOpenAi,

    /// <summary>A local Ollama server (nothing leaves the machine).</summary>
    Ollama,

    /// <summary>Deterministic template-based synthesis without external LLM calls.</summary>
    Template,

    /// <summary>Any OpenAI-compatible endpoint (Gemini, OpenRouter, vLLM…), see <see cref="OpenAiCompatibleOptions"/>.</summary>
    OpenAiCompatible
}

/// <summary>Picks the recap writer: Template (default), Ollama, Azure OpenAI or any OpenAI-compatible endpoint.</summary>
public sealed class AiProviderOptions
{
    /// <summary>Selected AI provider. Defaults to Template; external providers fall back to Template on failure.</summary>
    public AiProviderType Provider { get; init; } = AiProviderType.Template;

    /// <summary>Base URI for local or facility-hosted Ollama server.</summary>
    public string OllamaEndpoint { get; init; } = "http://localhost:11434";

    /// <summary>Ollama model for the text-only recap rewrite; a small instruct model is plenty.</summary>
    [Required]
    public string OllamaModel { get; init; } = "llama3.2:3b";
}
