namespace PoWatch.Application.Options;

/// <summary>
/// Any OpenAI-compatible chat endpoint used for recaps when AiProvider is OpenAiCompatible — e.g. Gemini
/// at https://generativelanguage.googleapis.com/v1beta/openai/ with model gemini-2.5-flash-lite.
/// </summary>
public sealed class OpenAiCompatibleOptions
{
    /// <summary>Base URL of the OpenAI-compatible API, ending in a slash.</summary>
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>API key. In production, supply via Azure Key Vault.</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>Model name the endpoint expects.</summary>
    public string Model { get; init; } = string.Empty;
}
