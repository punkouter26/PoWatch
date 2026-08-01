using Microsoft.JSInterop;
using PoWatch.Shared.Models;

namespace PoWatch.Client.Services;

// TODO(prune/absolute 2026-07-06): this type is currently orphaned — defined
// but not registered in any DI bootstrap and not injected anywhere in
// `src/PoWatch.Client`. Either:
//   (a) wire it via `builder.Services.AddSingleton<MonitoringLoopService>()`
//       in `PoWatch.Client/Program.cs` and consume it from ObserverHub.razor.cs,
//   (b) delete this file once you confirm nothing in-flight depends on it.
//
// Audit evidence: zero references outside this file. Last touched by an
// in-flight refactor; preserve until that lands or be removed explicitly.

/// <summary>
/// Encapsulates the inference loop orchestration: polling, frame capture, and event ingestion.
/// Separated from the UI layer for testability and cleaner separation of concerns.
/// </summary>
public sealed class MonitoringLoopService : IDisposable
{
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<MonitoringLoopService> _logger;
    private readonly PoWatchApiClient _apiClient;

    private CancellationTokenSource? _monitorCts;
    private bool _isRunning;
    private bool _isThinking;
    private DateTimeOffset? _startedAtUtc;
    private int _totalCycles;
    private int _skippedCycles;
    private int _structuredCycles;
    private long _minInferenceMs = long.MaxValue;
    private long _maxInferenceMs;
    private long _totalActiveMs;
    private readonly Queue<long> _latencyHistory = new();
    private long _p95LatencyMs;

    public bool IsRunning => _isRunning;
    public bool IsThinking => _isThinking;
    public DateTimeOffset? StartedAtUtc => _startedAtUtc;
    public int TotalCycles => _totalCycles;
    public int SkippedCycles => _skippedCycles;
    public int StructuredCycles => _structuredCycles;
    public long P95LatencyMs => _p95LatencyMs;

    public event EventHandler<MonitoringStateChange>? StateChanged;

    public MonitoringLoopService(IJSRuntime jsRuntime, ILogger<MonitoringLoopService> logger, PoWatchApiClient apiClient)
    {
        _jsRuntime = jsRuntime;
        _logger = logger;
        _apiClient = apiClient;
    }

    public void Start()
    {
        // Cancel AND dispose the previous source — Start/Stop cycles used to leak one
        // CancellationTokenSource (and its registered callbacks) per run.
        _monitorCts?.Cancel();
        _monitorCts?.Dispose();
        _monitorCts = new CancellationTokenSource();
        _startedAtUtc = DateTimeOffset.UtcNow;
        _isRunning = true;
        _totalCycles = 0;
        _skippedCycles = 0;
        _structuredCycles = 0;
        _minInferenceMs = long.MaxValue;
        _maxInferenceMs = 0;
        _totalActiveMs = 0;
        _latencyHistory.Clear();
        _p95LatencyMs = 0;
        OnStateChanged();
    }

    public void Stop()
    {
        _isRunning = false;
        _isThinking = false;
        _monitorCts?.Cancel();
        OnStateChanged();
    }

    public void SetThinking(bool thinking)
    {
        if (_isThinking != thinking)
        {
            _isThinking = thinking;
            OnStateChanged();
        }
    }

    public void RecordCycle(long inferenceMs, bool wasSkipped, bool wasStructured)
    {
        _totalCycles++;
        if (wasSkipped) _skippedCycles++;
        if (wasStructured) _structuredCycles++;

        if (inferenceMs > 0)
        {
            _totalActiveMs += inferenceMs;
            if (inferenceMs < _minInferenceMs) _minInferenceMs = inferenceMs;
            if (inferenceMs > _maxInferenceMs) _maxInferenceMs = inferenceMs;
            _latencyHistory.Enqueue(inferenceMs);
            if (_latencyHistory.Count > 100) _latencyHistory.Dequeue();
            var sorted = _latencyHistory.OrderBy(x => x).ToArray();
            _p95LatencyMs = sorted[(int)Math.Floor((sorted.Length - 1) * 0.95)];
        }
        OnStateChanged();
    }

    public void Dispose()
    {
        _monitorCts?.Cancel();
        _monitorCts?.Dispose();
        _monitorCts = null;
    }

    private void OnStateChanged() =>
        StateChanged?.Invoke(this, new MonitoringStateChange(
            IsRunning: _isRunning,
            IsThinking: _isThinking,
            TotalCycles: _totalCycles,
            SkippedCycles: _skippedCycles,
            StructuredCycles: _structuredCycles,
            P95LatencyMs: _p95LatencyMs));
}

public sealed record MonitoringStateChange(
    bool IsRunning,
    bool IsThinking,
    int TotalCycles,
    int SkippedCycles,
    int StructuredCycles,
    long P95LatencyMs);
