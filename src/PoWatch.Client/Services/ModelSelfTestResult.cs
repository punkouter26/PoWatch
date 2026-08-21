namespace PoWatch.Client.Services;

/// <summary>
/// Outcome of one per-model self-test, returned by <c>powatchInference.runModelTest</c>.
/// <para>
/// The test drives the same two calls the observation loop makes — load the model, generate once —
/// so the numbers here describe the real pipeline on the real device, not a parallel code path.
/// <see cref="Ok"/> answers only "did this model load and produce text on this machine"; the
/// quality gates that decide whether a caption is worth <em>recording</em> are reported separately
/// in <see cref="PipelineStatus"/>, because a small captioner routinely produces a fine sentence
/// that the gates decline, and failing the hardware check for that would be wrong.
/// </para>
/// </summary>
internal sealed class ModelSelfTestResult
{
    public string ModelKey { get; init; } = string.Empty;
    public string? ModelId { get; init; }
    public string? Label { get; init; }

    /// <summary>True when the model loaded and generated at least one character.</summary>
    public bool Ok { get; init; }

    /// <summary>How far the test got: registry, load, inference, done — or fixture, bridge, error.</summary>
    public string? Stage { get; init; }

    public string? Error { get; init; }

    /// <summary>Backend actually used, read after generating so a runtime webgpu → wasm escalation shows.</summary>
    public string? Device { get; init; }

    public string? Dtype { get; init; }
    public bool Fp16FallbackUsed { get; init; }
    public bool WebGpuPresent { get; init; }
    public string? GpuAdapterName { get; init; }
    public int? LoadMs { get; init; }
    public int? InferenceMs { get; init; }
    public int? TotalMs { get; init; }

    /// <summary>Bytes pulled over the wire during the load — the measured answer to "is it too big?".</summary>
    public long BytesFetched { get; init; }

    /// <summary>The model's verbatim reply to the test image.</summary>
    public string? RawOutput { get; init; }

    /// <summary>The observation pipeline's verdict on that reply — "OK", or the gate that rejected it.</summary>
    public string? PipelineStatus { get; init; }

    /// <summary>The fixed test image the model was asked about, so the reply can be judged against it.</summary>
    public string? TestFrameDataUrl { get; init; }
}
