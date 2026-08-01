namespace PoWatch.Client.Services;

/// <summary>
/// Marks which inference implementation is wired up for this build. Implementations:
/// <list type="bullet">
///   <item><see cref="WebGpuInferenceService"/> — real on-device inference.</item>
///   <item><see cref="MockInferenceService"/> — static mock (enabled by the UseMockAi flag).</item>
/// </list>
/// <para>
/// It no longer declares an <c>AnalyzeFrameAsync</c> method. Inference runs entirely in the
/// <c>inference-worker</c> Web Worker, reached through the <c>powatchInference</c> JS bridge — the
/// C# method was a parallel implementation of the same job that nothing ever called. What the
/// registration is actually for is the resolved service's identity: <see cref="IMockable"/> on it
/// drives the persistent MOCK DATA chip (AGENT.md §4).
/// </para>
/// </summary>
internal interface IInferenceService;
