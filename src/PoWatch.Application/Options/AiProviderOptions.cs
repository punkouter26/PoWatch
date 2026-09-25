namespace PoWatch.Application.Options;

/// <summary>Who writes the recap paragraph.</summary>
public enum AiProviderType
{
    /// <summary>Deterministic template-based synthesis without external LLM calls.</summary>
    Template,

    /// <summary>Azure OpenAI deployment, see <see cref="AzureOpenAiOptions"/>.</summary>
    AzureOpenAi
}

/// <summary>Picks the recap writer: Template (default) or Azure OpenAI.</summary>
public sealed class AiProviderOptions
{
    /// <summary>Selected AI provider. Defaults to Template; Azure OpenAI falls back to Template on failure.</summary>
    public AiProviderType Provider { get; init; } = AiProviderType.Template;
}
