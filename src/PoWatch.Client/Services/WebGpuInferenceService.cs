using System.Diagnostics;
using Microsoft.JSInterop;

namespace PoWatch.Client.Services;

/// <summary>
/// Runs a vision model entirely in-browser via transformers.js and WebGPU.
/// Zero cloud compute: the model executes locally — no image data leaves the browser.
/// Active when <c>FeatureFlags.UseMockAi = false</c> (default).
/// </summary>
internal sealed class WebGpuInferenceService : IInferenceService
{
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<WebGpuInferenceService> _logger;

    private sealed class JsInferenceResult
    {
        public string Output { get; set; } = string.Empty;
        public int TokenCount { get; set; }
        public double TokensPerSec { get; set; }
        public string ModelKey { get; set; } = string.Empty;
        public string Backend { get; set; } = string.Empty;
        public bool IsSignificantRaw { get; set; }
        public long ElapsedMs { get; set; }
    }

    public WebGpuInferenceService(IJSRuntime jsRuntime, ILogger<WebGpuInferenceService> logger)
    {
        _jsRuntime = jsRuntime;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<InferenceResult> AnalyzeFrameAsync(string base64ImageJpeg, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Starting WebGPU inference");
        var sw = Stopwatch.StartNew();

        try
        {
            var jsResult = await _jsRuntime.InvokeAsync<JsInferenceResult>(
                "PoWatchInference.runGemmaInference",
                cancellationToken,
                base64ImageJpeg).ConfigureAwait(false);

            sw.Stop();
            var rawOutput = jsResult?.Output ?? string.Empty;
            var inferenceMs = jsResult?.ElapsedMs > 0 ? jsResult.ElapsedMs : sw.ElapsedMilliseconds;

            _logger.LogInformation(
                "WebGPU inference completed. InferenceMs={InferenceMs} OutputLength={OutputLength} TokenCount={TokenCount} TPS={TokensPerSec:F2} Model={ModelKey} Backend={Backend}",
                inferenceMs, rawOutput.Length, jsResult?.TokenCount ?? 0, jsResult?.TokensPerSec ?? 0,
                jsResult?.ModelKey ?? "", jsResult?.Backend ?? "");

            var confidence = ExtractConfidence(rawOutput);

            return new InferenceResult(
                RawOutput: rawOutput,
                ParsedDescription: rawOutput.Trim(),
                Confidence: confidence,
                InferenceMs: inferenceMs,
                ErrorMessage: null,
                TokenCount: jsResult?.TokenCount ?? 0,
                TokensPerSecond: jsResult?.TokensPerSec ?? 0,
                ModelKey: jsResult?.ModelKey ?? string.Empty,
                Backend: jsResult?.Backend ?? string.Empty,
                IsSignificantRaw: jsResult?.IsSignificantRaw ?? false);
        }
        catch (JSException ex)
        {
            sw.Stop();
            _logger.LogError(ex, "WebGPU inference failed (JS error). InferenceMs={InferenceMs}", sw.ElapsedMilliseconds);
            return new InferenceResult(string.Empty, string.Empty, "error", sw.ElapsedMilliseconds, ex.Message);
        }
        catch (OperationCanceledException ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "WebGPU inference cancelled. InferenceMs={InferenceMs}", sw.ElapsedMilliseconds);
            return new InferenceResult(string.Empty, string.Empty, "error", sw.ElapsedMilliseconds, ex.Message);
        }
    }

    private static string ExtractConfidence(string raw)
    {
        if (raw.Contains("high confidence", StringComparison.OrdinalIgnoreCase)) return "high";
        if (raw.Contains("medium confidence", StringComparison.OrdinalIgnoreCase)) return "medium";
        if (raw.Contains("low confidence", StringComparison.OrdinalIgnoreCase)) return "low";
        return "medium";
    }
}
