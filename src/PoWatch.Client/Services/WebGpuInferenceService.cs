namespace PoWatch.Client.Services;

/// <summary>
/// The default (non-mock) inference registration. Active when <c>FeatureFlags.UseMockAi = false</c>.
/// <para>
/// Deliberately empty: the vision model runs entirely in-browser inside the
/// <c>inference-worker</c> Web Worker, driven from <c>ObserverHub</c> through the
/// <c>powatchInference</c> JS bridge. This class used to carry its own <c>AnalyzeFrameAsync</c>
/// JS-interop path — a second, unreferenced implementation of the same job, with its own DTO and
/// its own error handling to drift out of sync with the bridge the app actually uses.
/// </para>
/// <para>
/// It is still registered, because the absence of <see cref="IMockable"/> on the resolved
/// service is exactly what keeps the MOCK DATA chip hidden in a real build.
/// </para>
/// </summary>
internal sealed class WebGpuInferenceService : IInferenceService;
