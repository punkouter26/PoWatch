using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using PoWatch.Client.Services;
using PoWatch.Shared.Models;

namespace PoWatch.Client.Pages;

public partial class ObserverHub
{
    [Inject] private IOptions<ClientFeatureFlagsOptions> FeatureFlags { get; set; } = default!;

    private sealed record ModelOption(string Value, string Label);
    private static readonly IReadOnlyList<ModelOption> ModelOptions =
    [
        new("smolvlm-256m", "SmolVLM 256M"),
        new("smolvlm-500m", "SmolVLM 500M")
    ];

    private List<ObservationEventDto> streamItems = [];
    private ObserverRuntimeStateDto? observerState;
    private InferenceDiagnosticsSnapshot? inferenceDiagnostics;
    private bool muted = true;
    private bool thinking;
    private bool monitoring;
    private bool hasCameraFeed;
    private bool _hudExpanded;
    private ElementReference liveCameraFeed;
    private CancellationTokenSource? monitorCts;
    private string selectedModelKey = "smolvlm-256m";
    private DateTimeOffset? monitoringStartedAtUtc;
    private DateTimeOffset? lastSyncAtUtc;
    private string lastSyncStatus = "Standby";
    private string lastInferenceStatus = "Idle";
    private string lastAlertLevel = "Normal";
    private string lastAlertReason = "No alerts detected";
    private string lastDetectedSubject = "No person detected";
    private double lastConfidencePercent;
    private string lastConfidenceLabel = "Awaiting AI";
    private double lastMotionPercent;
    private string lastMotionLabel = "Still";

    private List<ThresholdAlertDto> _activeThresholdAlerts = [];
    private bool HasActiveThresholdAlerts => _activeThresholdAlerts.Count > 0;
    private bool ObservationLoopEnabled => observerState?.ObservationLoopEnabled ?? FeatureFlags.Value.ObservationLoopEnabled;

    private string MonitoringStateLabel => thinking ? "Analysing" : monitoring ? "Live" : "Standby";
    private string SelectedModelLabel => ModelOptions.FirstOrDefault(option => option.Value == selectedModelKey)?.Label ?? selectedModelKey;
    private string LatestActivityLabel => streamItems.FirstOrDefault()?.Activity ?? "Waiting for first event";
    private string LatestTimestampLabel => streamItems.FirstOrDefault() is { } latest
        ? latest.ObservedAtUtc.ToLocalTime().ToString("HH:mm:ss")
        : "No activity recorded";
    private int SessionEventCount => monitoringStartedAtUtc is { } startedAt
        ? streamItems.Count(item => item.ObservedAtUtc >= startedAt)
        : 0;
    private string RecordingDurationLabel => monitoringStartedAtUtc is { } startedAt
        ? FormatElapsed(DateTimeOffset.UtcNow - startedAt)
        : "00:00:00";
    private string PersonDetectedLabel => lastDetectedSubject;
    private string AlertLevelLabel => lastAlertLevel;
    private string AlertLevelClass => lastAlertLevel switch
    {
        "Urgent" => "monitor-metric-card--alert",
        "Watch" => "monitor-metric-card--watch",
        _ => "monitor-metric-card--good"
    };
    private string ConfidenceDisplayLabel => lastConfidencePercent > 0
        ? $"{lastConfidencePercent:0}% · {lastConfidenceLabel}"
        : lastConfidenceLabel;
    private string CameraHealthLabel => !hasCameraFeed
        ? "Offline"
        : inferenceDiagnostics is { PreviewWidth: > 0, PreviewHeight: > 0 }
            ? $"Healthy · {inferenceDiagnostics.PreviewWidth}×{inferenceDiagnostics.PreviewHeight}"
            : "Healthy · Preview attached";
    private string ConnectionStatusLabel => monitoring ? lastSyncStatus : "Standby";
    private string MotionDisplayLabel => $"{lastMotionLabel} · {lastMotionPercent:0}%";
    private string PrivacyStatusLabel => muted ? "Video only · Muted" : "Video only · Voice on";
    private string SessionDurationText => monitoringStartedAtUtc is { } startedAt
        ? FormatElapsed(DateTimeOffset.UtcNow - startedAt)
        : "--:--:--";

    private const string PollingStorageKey = "pw_polling_interval";
    private long _hudCycleMs = 0;
    private double _emaInferenceMs = 0.0;
    private int _hudTokenCount = 0;
    private string _hudTokensPerSecond = "0";
    private string _hudMemory = "--";
    private string _hudModel = "--";
    private string _hudBackend = "--";
    private string _gpuAdapterVendor = string.Empty;
    private string _gpuAdapterName = "--";
    private int _frameCount = 0;
    private int _livePollingSeconds;
    private string _selectedGpuPreference = "default";
    private readonly Queue<long> _latencyHistory = new();
    private long _p95LatencyMs;
    private int _totalCycles;
    private int _skippedCycles;
    private int _structuredCycles;
    private long _minInferenceMs = long.MaxValue;
    private long _maxInferenceMs;
    private long _totalActiveMs;

    private string DtypeDisplay => inferenceDiagnostics?.Dtype?.ToUpperInvariant() ?? "--";
    private string LoadTimeDisplay => inferenceDiagnostics?.LoadDurationMs is int ldms ? $"{ldms:N0} ms" : "--";
    private string InferCountDisplay => $"{inferenceDiagnostics?.InferenceCount ?? 0}";
    private string LastMsDisplay => inferenceDiagnostics?.LastInferenceMs is int lms ? $"{lms:N0} ms" : "--";
    private string Fp16FallbackDisplay => inferenceDiagnostics is null ? "--" : inferenceDiagnostics.Fp16FallbackUsed ? "Yes · fp32 used" : "No · fp16 OK";
    private string SkipRateDisplay => _totalCycles > 0 ? $"{_skippedCycles * 100 / _totalCycles:0}% ({_skippedCycles}/{_totalCycles})" : "--";
    private string StructRateDisplay => _totalCycles > 0 ? $"{_structuredCycles * 100 / _totalCycles:0}% ({_structuredCycles}/{_totalCycles})" : "--";
    private string MinMaxInferDisplay => _maxInferenceMs > 0 ? $"{_minInferenceMs}/{_maxInferenceMs} ms" : "--";
    private string DriftDisplay => _emaInferenceMs > 0 && _hudCycleMs > 0 ? $"{Math.Abs(_hudCycleMs - (long)_emaInferenceMs)} ms" : "--";
    private string DutyCycleDisplay
    {
        get
        {
            if (!monitoring || monitoringStartedAtUtc is null || _totalActiveMs == 0) return "--";
            var totalMs = (DateTimeOffset.UtcNow - monitoringStartedAtUtc.Value).TotalMilliseconds;
            return totalMs > 0 ? $"{_totalActiveMs * 100.0 / totalMs:0}%" : "--";
        }
    }
    private string P95LatencyDisplay => _p95LatencyMs > 0 ? $"{_p95LatencyMs:N0} ms" : "--";
}