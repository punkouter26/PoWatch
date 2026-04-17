namespace PoWatch.Client.Services;

/// <summary>
/// Contract for local AI inference. Implementations:
/// - <see cref="WebGpuInferenceService"/> — real Gemma via transformers.js + WebGPU
/// - <see cref="MockInferenceService"/> — static mock for UI/E2E testing (enabled by UseMockAi flag)
/// </summary>
internal interface IInferenceService
{
    /// <summary>
    /// Runs inference on a webcam frame captured as a Base64-encoded JPEG.
    /// Returns the model's natural-language output.
    /// </summary>
    Task<InferenceResult> AnalyzeFrameAsync(string base64ImageJpeg, CancellationToken cancellationToken = default);
}
