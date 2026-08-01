using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using PoWatch.Client.Services;
using PoWatch.Shared.Models;

namespace PoWatch.Client.Pages;

/// <summary>
/// Current alert level for the observer session. Replaces the previous raw "Urgent"/"Watch"/"Normal"
/// strings that were set in one file and switch-matched in another (a typo silently fell through to
/// the "good" style). The compiler now enforces every case.
/// </summary>
public enum AlertLevel
{
    Normal,
    Watch,
    Urgent
}

public partial class ObserverHub
{
    [Inject] private IOptions<ClientFeatureFlagsOptions> FeatureFlags { get; set; } = default!;
    [Inject] private HttpClient Http { get; set; } = default!;

    private sealed record ModelOption(string Value, string Label);
    // Populated in OnInitializedAsync from the shared /model-registry.json (single source of truth shared
    // with the inference worker, rule 1.5) — no longer a hardcoded copy of the worker's list.
    private IReadOnlyList<ModelOption> ModelOptions = [];

    private List<ObservationEventDto> streamItems = [];
    private ObserverRuntimeStateDto? observerState;
    private InferenceDiagnosticsSnapshot? inferenceDiagnostics;
    private bool muted = true;
    private bool thinking;
    private bool monitoring;
    private bool hasCameraFeed;
    private bool _settingsOpen;
    private ElementReference liveCameraFeed;
    private CancellationTokenSource? monitorCts;
    private string selectedModelKey = "smolvlm-256m";
    private DateTimeOffset? monitoringStartedAtUtc;
    private DateTimeOffset? lastSyncAtUtc;
    private string lastSyncStatus = "Standby";
    private string lastInferenceStatus = "Idle";
    private AlertLevel lastAlertLevel = AlertLevel.Normal;
    private string lastAlertReason = "No alerts detected";
    private string lastDetectedSubject = "No person detected";
    private double lastConfidencePercent;
    private string lastConfidenceLabel = "Awaiting AI";
    private double lastMotionPercent;
    private string lastMotionLabel = "Still";

    /// <summary>Verbatim model reply from the most recent quality-gate rejection, surfaced in the UI.</summary>
    private string? lastRejectedOutput;

    /// <summary>
    /// True once enough cycles have run to conclude the model is producing nothing usable. A high
    /// skip rate is the failure mode that previously presented as a healthy, idle-looking loop.
    /// </summary>
    private bool AllCyclesRejected => _totalCycles >= 3 && _structuredCycles == 0;

    private List<ThresholdAlertDto> _activeThresholdAlerts = [];
    private bool HasActiveThresholdAlerts => _activeThresholdAlerts.Count > 0;
    private bool ObservationLoopEnabled => observerState?.ObservationLoopEnabled ?? FeatureFlags.Value.ObservationLoopEnabled;

    // ── Start-up failure latch (Fix #2/#8): when Start watching fails (no webcam, permission
    // denied, model not loadable, …) we previously just snapped back to Standby and toasted
    // a 5-second message nobody reads. Now we keep a pinned card until the operator clears it,
    // so the kiosk never sits there looking like it's "monitoring" when nothing happened. ──
    private string? _lastAttemptFailure;
    private DateTimeOffset? _lastAttemptAtUtc;
    private bool HasLastAttemptFailure => !string.IsNullOrWhiteSpace(_lastAttemptFailure);
    private string LastAttemptLabel => HasLastAttemptFailure && _lastAttemptAtUtc is { } ts
        ? $"Last attempt failed at {ts.ToLocalTime():HH:mm:ss} — {_lastAttemptFailure}"
        : "Press Start to begin watching the room.";

    // ── Pipeline-health latch (idea 7): the one failure class that matters on a kiosk — GPU /
    // inference / server dropout — surfaced as a persistent banner that stays until dismissed,
    // rather than a toast nobody is watching.
    private CancellationTokenSource? _keepAliveCts;
    private bool _pipelineHealthLatched;
    private string _pipelineHealthLevel = "warn"; // "warn" | "error"
    private string _pipelineHealthMessage = string.Empty;
    private bool _fp16WarningLatched;

    private void LatchPipelineHealth(string level, string message)
    {
        // Errors win over warnings; a latched error is not downgraded by a later warning.
        if (_pipelineHealthLatched && _pipelineHealthLevel == "error" && level != "error") return;
        _pipelineHealthLevel = level;
        _pipelineHealthMessage = message;
        _pipelineHealthLatched = true;
    }

    private string MonitoringStateLabel => thinking ? "Analysing" : monitoring ? "Live" : "Standby";
    private string SelectedModelLabel => ModelOptions.FirstOrDefault(option => option.Value == selectedModelKey)?.Label ?? selectedModelKey;
    private string LatestActivityLabel => streamItems.FirstOrDefault()?.Activity ?? "Waiting for first event";
    private string LatestTimestampLabel => streamItems.FirstOrDefault() is { } latest
        ? latest.ObservedAtUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture)
        : "No activity recorded";
    private int SessionEventCount => monitoringStartedAtUtc is { } startedAt
        ? streamItems.Count(item => item.ObservedAtUtc >= startedAt)
        : 0;
    private string PersonDetectedLabel => lastDetectedSubject;
    private string AlertLevelLabel => lastAlertLevel.ToString();
    private string AlertLevelClass => lastAlertLevel switch
    {
        AlertLevel.Urgent => "monitor-metric-card--alert",
        AlertLevel.Watch => "monitor-metric-card--watch",
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

    // ── Calm-state model (UX Win #1 / #6): the single caregiver-facing answer to "is the room OK?".
    // Idle = not watching · Calm = watching, nothing notable · Watch = a notable moment · Alert = urgent.
    internal enum RoomStatus { Idle, Calm, Watch, Alert }

    private RoomStatus CurrentRoomStatus =>
        !monitoring ? RoomStatus.Idle
        : lastAlertLevel == AlertLevel.Urgent ? RoomStatus.Alert
        : lastAlertLevel == AlertLevel.Watch ? RoomStatus.Watch
        : RoomStatus.Calm;

    private string RoomStatusKey => CurrentRoomStatus.ToString().ToLowerInvariant();

    private string RoomStatusHeadline => CurrentRoomStatus switch
    {
        RoomStatus.Idle => "Ready to watch",
        RoomStatus.Calm => "All calm",
        RoomStatus.Watch => "One notable moment",
        RoomStatus.Alert => "Needs attention",
        _ => "—"
    };

    // The hero subline is the ONLY live summary line (the Live Monitor panel was consolidated
    // away) — while watching it carries the person-detection state too.
    // Fix #3: when monitoring isn't running but the most recent attempt failed, the subline
    // surfaces that breadcrumb so the operator knows the system isn't watching, not "calm".
    private string RoomStatusSubline => CurrentRoomStatus switch
    {
        RoomStatus.Idle => HasLastAttemptFailure ? LastAttemptLabel : "Press Start to begin watching the room.",
        RoomStatus.Calm => string.IsNullOrWhiteSpace(lastSyncStatus) || lastSyncStatus == "Standby"
            ? $"Watching quietly · {PersonDetectedLabel}"
            : $"Watching quietly · {PersonDetectedLabel} · {lastSyncStatus}",
        RoomStatus.Watch => $"{LatestActivityLabel} · {LatestTimestampLabel}",
        RoomStatus.Alert => string.IsNullOrWhiteSpace(lastAlertReason) ? "Something unusual just happened." : lastAlertReason,
        _ => string.Empty
    };

    // Plain-language confidence for non-technical caregivers (Win #3): words first, number secondary.
    private static string ConfidenceWord(double percent) => percent switch
    {
        >= 75 => "High confidence",
        >= 45 => "Moderate confidence",
        > 0 => "Low confidence",
        _ => "Awaiting AI"
    };
    private string ConfidenceTrustLabel => lastConfidencePercent > 0
        ? $"{ConfidenceWord(lastConfidencePercent)} · {lastConfidencePercent:0}%"
        : lastConfidenceLabel;

    // ── Full-screen alert takeover (Win #4): captured when an outlier fires so the overlay can
    // show the frame, the plain-language reason, and a single Acknowledge action.
    private bool _alertOverlayVisible;
    private string _alertOverlaySubject = string.Empty;
    private string _alertOverlaySubjectId = string.Empty;
    private string _alertOverlayReason = string.Empty;
    private string _alertOverlayActivity = string.Empty;
    private string _alertOverlayTime = string.Empty;
    private string? _alertOverlayImage;

    private void AcknowledgeAlertOverlay()
    {
        _alertOverlayVisible = false;
        // Locally downgrade the surfaced alert so the calm hero returns. Server records are untouched;
        // this is the caregiver saying "I've seen it", not a clinical acknowledgement of the event.
        lastAlertLevel = AlertLevel.Normal;
        lastAlertReason = "Acknowledged — watching again";
    }

    private void ViewAlertSubject()
    {
        _alertOverlayVisible = false;
        if (!string.IsNullOrWhiteSpace(_alertOverlaySubjectId))
            Navigation.NavigateTo($"/identity?focus={Uri.EscapeDataString(_alertOverlaySubjectId)}");
    }

    // Auto-detect the current shift window from local time so "End shift" is genuinely one tap (Win #5).
    private static string DetectCurrentShift()
    {
        var hour = DateTimeOffset.Now.Hour;
        return hour is >= 6 and < 14 ? "Morning"
            : hour is >= 14 and < 22 ? "Afternoon"
            : "Night";
    }

    private void StartHandoff() =>
        Navigation.NavigateTo($"/archives?handoff=1&shift={DetectCurrentShift()}");

    private const string PollingStorageKey = "pw_polling_interval";
    private double _emaInferenceMs = 0.0;
    // Sane default until OnInitializedAsync loads the persisted preference — the settings
    // drawer can render before that completes and must not show "0 s".
    private int _livePollingSeconds = 10;
    private string _selectedGpuPreference = "default";
    private readonly Queue<long> _latencyHistory = new();
    private readonly long[] _p95Buffer = new long[100]; // reused each cycle; matches _latencyHistory cap
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

    // ── Live model-telemetry strip under the Room Feed camera ──
    private string ModelLoadStateDisplay
    {
        get
        {
            var state = inferenceDiagnostics?.LoadState;
            if (string.IsNullOrWhiteSpace(state)) return "Not loaded yet";
            var label = char.ToUpperInvariant(state[0]) + state[1..];
            return inferenceDiagnostics?.LoadDurationMs is int ms and > 0
                ? $"{label} · loaded in {ms / 1000.0:0.0} s"
                : label;
        }
    }
    private string EngineDisplay => inferenceDiagnostics?.Device is { Length: > 0 } device
        ? $"{device.ToUpperInvariant()} · {DtypeDisplay}"
        : "--";
    private string EngineSubDisplay => inferenceDiagnostics?.GpuAdapterName is { Length: > 0 } gpu
        ? gpu
        : inferenceDiagnostics?.WebGpuPresent == true ? "WebGPU available" : "No WebGPU — CPU only";
    private bool EngineDegraded => inferenceDiagnostics is not null
        && (inferenceDiagnostics.Fp16FallbackUsed
            || string.Equals(inferenceDiagnostics.Device, "wasm", StringComparison.OrdinalIgnoreCase));
    private string AvgCycleDisplay => _emaInferenceMs > 0 ? $"avg cycle {_emaInferenceMs:N0} ms" : "avg cycle --";
}
