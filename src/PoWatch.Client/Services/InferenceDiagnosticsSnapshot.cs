namespace PoWatch.Client.Services;

internal sealed class InferenceDiagnosticsSnapshot
{
    public string ModelId { get; init; } = string.Empty;
    public string LoadState { get; init; } = string.Empty;
    public string? LoadError { get; init; }
    public string? Device { get; init; }
    public string? Dtype { get; init; }
    public bool Fp16FallbackUsed { get; init; }
    public int? LoadDurationMs { get; init; }
    public int InferenceCount { get; init; }
    public int? LastInferenceMs { get; init; }
    public string? LastInferenceTimestamp { get; init; }
    public string? LastInferenceOutput { get; init; }
    public bool WebGpuPresent { get; init; }
    public string? GpuAdapterVendor { get; init; }
    public string? GpuAdapterName { get; init; }
    public string? JsHeapMb { get; init; }
    public bool StreamActive { get; init; }
    public int PreviewWidth { get; init; }
    public int PreviewHeight { get; init; }
}