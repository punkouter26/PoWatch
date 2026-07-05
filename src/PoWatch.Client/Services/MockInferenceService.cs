using System.Diagnostics;

namespace PoWatch.Client.Services;

/// <summary>
/// Mock inference service that returns pre-canned responses.
/// Active when <c>FeatureFlags.UseMockAi = true</c> (set in wwwroot/appsettings.json).
/// Used for UI testing and E2E tests — avoids real WebGPU dependency.
/// </summary>
internal sealed class MockInferenceService : IInferenceService, IMockable
{
    public string MockLabel => "AI inference";

    private static readonly string[] MockDescriptions =
    [
        "<SUBJECT:subject-001> One person seated at a desk working on a laptop. Good lighting from the window.",
        "Empty room. Desk is clear. No persons detected. Natural light from behind.",
        "<SUBJECT:subject-002> Two people visible. One standing near the whiteboard. Room appears to be an office.",
        "<SUBJECT:subject-001> One person at a desk. Multiple monitors visible. Moderate overhead lighting.",
        "Empty room. Chair is pushed back. Subject has left the frame.",
        "<SUBJECT:subject-001> Person re-entered the room and is seated again. Laptop open."
    ];

    private readonly ILogger<MockInferenceService> _logger;

    public MockInferenceService(ILogger<MockInferenceService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<InferenceResult> AnalyzeFrameAsync(string base64ImageJpeg, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("MockInferenceService returning mock response (UseMockAi=true)");

#pragma warning disable CA5394 // Non-security randomness; this method is a mock/test helper only
        var index = Random.Shared.Next(MockDescriptions.Length);
        var description = MockDescriptions[index];
        var inferenceMs = Random.Shared.Next(200, 1200);
#pragma warning restore CA5394

        var tokenCount = System.Text.RegularExpressions.Regex.Count(description, @"\b\w+\b");
        var tokensPerSecond = inferenceMs > 0 ? tokenCount * 1000.0 / inferenceMs : 0;
        var isSignificantRaw = description.Contains("left", StringComparison.OrdinalIgnoreCase)
            || description.Contains("re-entered", StringComparison.OrdinalIgnoreCase)
            || description.Contains("two people", StringComparison.OrdinalIgnoreCase)
            || description.Contains("empty room", StringComparison.OrdinalIgnoreCase);

        var result = new InferenceResult(
            RawOutput: description,
            ParsedDescription: description,
            Confidence: "medium",
            InferenceMs: inferenceMs,
            ErrorMessage: null,
            TokenCount: tokenCount,
            TokensPerSecond: tokensPerSecond,
            ModelKey: "mock",
            Backend: "mock",
            IsSignificantRaw: isSignificantRaw);

        return Task.FromResult(result);
    }
}
