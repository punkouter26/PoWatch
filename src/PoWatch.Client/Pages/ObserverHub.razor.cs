using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using PoWatch.Shared.Models;
using Radzen;
using Radzen.Blazor;

namespace PoWatch.Client.Pages;

public partial class ObserverHub
{
    private sealed record ModelOption(string Value, string Label);
    private static readonly IReadOnlyList<ModelOption> ModelOptions =
    [
        new("smolvlm-256m", "SmolVLM 256M"),
        new("smolvlm-500m", "SmolVLM 500M"),
        new("lfm2-vl-450m", "LFM2.5-VL 450M"),
        new("qwen2.5-vl-2b", "Qwen2.5-VL 2B")
    ];

    private List<ObservationEventDto> streamItems = [];
    private ObserverRuntimeStateDto? observerState;
    private InferenceDiagnosticsSnapshot? inferenceDiagnostics;
    private bool muted = true;
    private bool thinking;
    private bool monitoring;
    private bool hasCameraFeed;
    private bool devToolsExpanded;
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

    protected override async Task OnInitializedAsync()
    {
        await RefreshStateAsync();
        await RefreshTimelineAsync();
        await RefreshDiagnosticsAsync();
        muted = !(observerState?.TtsAnnouncementsEnabled ?? false);
    }

    private async Task StartMonitoringAsync()
    {
        await RefreshStateAsync();
        await RefreshTimelineAsync();

        monitorCts?.Cancel();
        monitorCts = new CancellationTokenSource();

        monitoringStartedAtUtc = DateTimeOffset.UtcNow;
        lastSyncAtUtc = null;
        lastSyncStatus = "Connecting...";
        lastInferenceStatus = "Starting preview...";
        lastAlertLevel = "Normal";
        lastAlertReason = "No alerts detected";
        lastDetectedSubject = "Awaiting first detection";
        lastConfidencePercent = 0;
        lastConfidenceLabel = "Awaiting AI";
        lastMotionPercent = 0;
        lastMotionLabel = "Still";

        monitoring = true;
        await InvokeAsync(StateHasChanged);

        var previewStatus = await JS.InvokeAsync<string>("powatchInference.startPreview", liveCameraFeed);
        if (!string.Equals(previewStatus, "OK", StringComparison.OrdinalIgnoreCase))
        {
            monitoring = false;
            lastSyncStatus = "Camera unavailable";
            lastInferenceStatus = previewStatus;
            NotificationService.Notify(NotificationSeverity.Warning, "Camera", previewStatus, duration: 5000);
            await RefreshDiagnosticsAsync();
            await InvokeAsync(StateHasChanged);
            return;
        }

        hasCameraFeed = true;
        await RefreshDiagnosticsAsync();

        _ = Task.Run(() => RunMonitorLoopAsync(monitorCts.Token));
        _ = Task.Run(() => RunMonitorHeartbeatAsync(monitorCts.Token));
    }

    private async Task StopMonitoringAsync()
    {
        monitoring = false;
        thinking = false;
        hasCameraFeed = false;
        lastSyncStatus = "Paused";
        lastInferenceStatus = "Stopped";

        monitorCts?.Cancel();

        await JS.InvokeVoidAsync("powatchInference.stopMonitor");
        await RefreshDiagnosticsAsync();
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnModelSwitched(object _)
    {
        await JS.InvokeVoidAsync("powatchInference.setModel", selectedModelKey);
        NotificationService.Notify(NotificationSeverity.Info, "Model", "Will load on next inference.", duration: 3000);
    }

    private async Task RunMonitorLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RunSingleCycleAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Ensure 'thinking' is never permanently stuck true after an unexpected exception.
                thinking = false;
                NotificationService.Notify(NotificationSeverity.Error, "Inference Error", ex.Message, duration: 6000);
                await InvokeAsync(StateHasChanged);
            }

            var delaySeconds = Math.Max(1, observerState?.PollIntervalSeconds ?? 10);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunSingleCycleAsync(CancellationToken cancellationToken)
    {
        thinking = true;
        lastInferenceStatus = "Analysing frame...";
        await InvokeAsync(StateHasChanged);

        var inference = await JS.InvokeAsync<InferenceBridgeResult>(
            "powatchInference.captureAndInfer",
            cancellationToken,
            "Observe the person or room. Reply strictly as: LABEL: <5 word activity summary> | NOTE: <one clinical sentence describing what you see>",
            liveCameraFeed);

        lastInferenceStatus = inference.Status;
        lastMotionPercent = inference.MotionScore ?? 0;
        lastMotionLabel = string.IsNullOrWhiteSpace(inference.MotionLevel) ? "Still" : inference.MotionLevel;

        if (!inference.IsAvailable)
        {
            thinking = false;
            lastConfidencePercent = 0;
            lastConfidenceLabel = "Awaiting AI";

            // Silent skips — don't show as errors, preserve current status
            if (inference.Status.StartsWith("Frame unchanged", StringComparison.OrdinalIgnoreCase) ||
                inference.Status.StartsWith("Low-quality", StringComparison.OrdinalIgnoreCase))
            {
                lastSyncStatus = inference.Status.StartsWith("Frame unchanged", StringComparison.OrdinalIgnoreCase)
                    ? "No sync needed"
                    : "Awaiting clearer frame";
                await RefreshDiagnosticsAsync();
                await InvokeAsync(StateHasChanged);
                return;
            }

            if (string.Equals(inference.Status, "Webcam unavailable in this browser. Fallback preview active.", StringComparison.OrdinalIgnoreCase))
            {
                monitoring = false;
                hasCameraFeed = false;
                lastSyncStatus = "Camera unavailable";
                monitorCts?.Cancel();
                NotificationService.Notify(NotificationSeverity.Error, "Camera", inference.Status, duration: 0);
            }
            else
            {
                lastSyncStatus = "Sync blocked";
                NotificationService.Notify(NotificationSeverity.Warning, "Inference", inference.Status, duration: 5000);
            }

            await RefreshDiagnosticsAsync();
            await InvokeAsync(StateHasChanged);
            return;
        }

        lastConfidencePercent = Math.Round(inference.ConfidenceScore * 100d, 0);
        lastConfidenceLabel = string.IsNullOrWhiteSpace(inference.ConfidenceLabel) ? "Structured" : inference.ConfidenceLabel;
        if (!string.IsNullOrWhiteSpace(inference.SubjectHint))
        {
            lastDetectedSubject = inference.SubjectHint!;
        }

        var result = await ApiClient.IngestObservationAsync(new IngestObservationRequestDto
        {
            SubjectHint = inference.SubjectHint,
            Activity = inference.Activity,
            ClinicalPayload = inference.ClinicalPayload,
            IsSignificant = inference.IsSignificant,
            SignificantReason = inference.SignificantReason
        }, cancellationToken);

        thinking = false;

        if (result is not null)
        {
            if (!string.IsNullOrWhiteSpace(result.SubjectDisplayName))
            {
                lastDetectedSubject = result.SubjectDisplayName;
            }

            if (result.IsOutlier)
            {
                lastAlertLevel = "Urgent";
                lastAlertReason = result.Detail;
            }
            else if (inference.IsSignificant)
            {
                lastAlertLevel = "Watch";
                lastAlertReason = result.Detail;
            }
            else
            {
                lastAlertLevel = "Normal";
                lastAlertReason = "No active alert";
            }

            if (result.Dropped)
            {
                lastSyncStatus = "Dropped by API";
            }
            else if (result.SkippedAsRedundant)
            {
                lastSyncAtUtc = DateTimeOffset.UtcNow;
                lastSyncStatus = $"Already synced · {lastSyncAtUtc.Value.ToLocalTime():HH:mm:ss}";
            }
            else
            {
                lastSyncAtUtc = DateTimeOffset.UtcNow;
                lastSyncStatus = $"Synced · {lastSyncAtUtc.Value.ToLocalTime():HH:mm:ss}";
            }
        }
        else
        {
            lastSyncStatus = "API unavailable";
        }

        if (result is not null && !result.Dropped && !result.SkippedAsRedundant && inference.IsSignificant)
        {
            await TryUploadEvidenceAsync(result.SubjectId, $"{result.SubjectDisplayName}: {inference.Activity}");
        }

        if (result is not null && !muted && !result.SkippedAsRedundant)
        {
            await AnnounceAsync(result.SubjectDisplayName, string.IsNullOrWhiteSpace(inference.SubjectHint));
        }

        await RefreshTimelineAsync();
        await RefreshDiagnosticsAsync();
        await InvokeAsync(StateHasChanged);
    }

    private async Task InjectEventAsync()
    {
        thinking = true;

        var result = await ApiClient.IngestObservationAsync(new IngestObservationRequestDto
        {
            SubjectHint = "Kim",
            Activity = "Desk Work",
            ClinicalPayload = "<S>Known subject entered and resumed desk work.<E>",
            IsSignificant = true,
            SignificantReason = "Known person entered"
        });

        thinking = false;

        if (result is not null && !result.Dropped && !result.SkippedAsRedundant)
        {
            await TryUploadEvidenceAsync(result.ImageReference, $"{result.SubjectDisplayName}: Desk Work");
        }

        if (result is not null && !muted && !result.SkippedAsRedundant)
        {
            await AnnounceAsync(result.SubjectDisplayName, false);
        }

        await RefreshTimelineAsync();
    }

    private async Task InjectOutlierAsync()
    {
        thinking = true;

        var result = await ApiClient.IngestObservationAsync(new IngestObservationRequestDto
        {
            SubjectHint = null,
            Activity = "Unknown movement",
            ClinicalPayload = "malformed payload",
            IsSignificant = true,
            SignificantReason = "Clinical outlier"
        });

        thinking = false;

        if (result is not null && !result.Dropped)
        {
            await TryUploadEvidenceAsync(result.ImageReference, $"{result.SubjectDisplayName}: Clinical outlier");
        }

        if (result is not null && !muted)
        {
            await AnnounceAsync(result.SubjectDisplayName, true);
        }

        await RefreshTimelineAsync();
    }

    private async Task AnnounceAsync(string subjectDisplayName, bool isUnknown)
    {
        if (isUnknown)
        {
            await JS.InvokeVoidAsync("powatchAudio.playChirp");
        }

        var message = isUnknown
            ? $"New subject detected: {subjectDisplayName}"
            : $"Subject identified: {subjectDisplayName}";

        await JS.InvokeVoidAsync("powatchAudio.announce", message);
    }

    private async Task TryUploadEvidenceAsync(string? imageReference, string label)
    {
        if (string.IsNullOrWhiteSpace(imageReference))
        {
            return;
        }

        try
        {
            var access = await ApiClient.GetBlobUploadAccessForPathAsync(imageReference);
            if (access is not null && !string.IsNullOrWhiteSpace(access.SasUrl))
            {
                await JS.InvokeVoidAsync("powatchBlobUpload.uploadSvgPlaceholder", access.SasUrl, label);
            }
        }
        catch
        {
            NotificationService.Notify(NotificationSeverity.Warning, "Evidence Upload", "Azurite upload unavailable. Start the Docker storage emulator.", duration: 8000);
        }
    }

    private async Task RefreshStateAsync()
    {
        observerState = await ApiClient.GetObserverStateAsync();
    }

    private async Task RefreshDiagnosticsAsync()
    {
        try
        {
            inferenceDiagnostics = await JS.InvokeAsync<InferenceDiagnosticsSnapshot>("powatchInference.getInferenceDiagnostics");
        }
        catch
        {
            // Ignore diagnostics failures; the monitor can still run without the extra telemetry.
        }
    }

    private async Task RefreshTimelineAsync()
    {
        var chapter = await ApiClient.GetChapterAsync(DateOnly.FromDateTime(DateTime.UtcNow));
        streamItems = chapter?.Timeline is not null
            ? chapter.Timeline.OrderByDescending(x => x.ObservedAtUtc).Take(50).ToList()
            : [];
    }

    private async Task RunMonitorHeartbeatAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Ignore expected shutdown.
        }
    }

    private static string FormatElapsed(TimeSpan elapsed) =>
        $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";

    private void StreamRowRender(RowRenderEventArgs<ObservationEventDto> args)
    {
        args.Expandable = args.Data is not null && !string.IsNullOrWhiteSpace(args.Data.ClinicalDescription);
    }

    public async ValueTask DisposeAsync()
    {
        monitorCts?.Cancel();

        await JS.InvokeVoidAsync("powatchInference.stopMonitor");
    }

    private sealed class InferenceBridgeResult
    {
        public bool IsAvailable { get; init; }
        public string Status { get; init; } = string.Empty;
        public string? SubjectHint { get; init; }
        public string Activity { get; init; } = "Unknown";
        public string ClinicalPayload { get; init; } = string.Empty;
        public bool IsSignificant { get; init; }
        public string? SignificantReason { get; init; }
        public double ConfidenceScore { get; init; }
        public string ConfidenceLabel { get; init; } = string.Empty;
        public double? MotionScore { get; init; }
        public string MotionLevel { get; init; } = string.Empty;
    }

    private sealed class InferenceDiagnosticsSnapshot
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
        public bool StreamActive { get; init; }
        public int PreviewWidth { get; init; }
        public int PreviewHeight { get; init; }
    }
}
