namespace PoWatch.Client.Services;

/// <summary>Result of a single inference cycle.</summary>
public sealed record InferenceResult(
    string RawOutput,
    string ParsedDescription,
    string Confidence,
    long InferenceMs,
    string? ErrorMessage = null,
    int TokenCount = 0,
    double TokensPerSecond = 0,
    string ModelKey = "",
    string Backend = "",
    bool IsSignificantRaw = false)
{
    public bool IsSuccess => ErrorMessage is null;
}
