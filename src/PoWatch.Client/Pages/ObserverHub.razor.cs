using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using PoWatch.Client.Services;
using PoWatch.Shared.Models;
using Radzen;
using System.Net.Http;

namespace PoWatch.Client.Pages;

public partial class ObserverHub
{
    protected override async Task OnInitializedAsync()
    {
        await RefreshStateAsync();
        await RefreshTimelineAsync();
        await RefreshDiagnosticsAsync();
        await LoadSubjectsAsync();
        muted = true;

        // Restore persisted polling interval; fall back to appSettings default
        try
        {
            var stored = await JS.InvokeAsync<string?>("PoWatchStorage.get", PollingStorageKey);
            _livePollingSeconds = int.TryParse(stored, out var parsed) && parsed is >= 5 and <= 120
                ? parsed
                : FeatureFlags.Value.PollingIntervalSeconds;
        }
        catch
        {
            _livePollingSeconds = FeatureFlags.Value.PollingIntervalSeconds;
        }
    }

    private async Task StartMonitoringAsync()
    {
        await RefreshStateAsync();
        await RefreshTimelineAsync();

        if (!ObservationLoopEnabled)
        {
            lastSyncStatus = "Disabled by operator";
            lastInferenceStatus = "Observation loop disabled";
            NotificationService.Notify(
                NotificationSeverity.Warning,
                "Monitoring Disabled",
                "ObservationLoopEnabled is off. Re-enable the feature flag before starting monitoring.",
                duration: 5000);
            await InvokeAsync(StateHasChanged);
            return;
        }

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

        // Reset inference analytics counters
        _totalCycles = 0;
        _skippedCycles = 0;
        _structuredCycles = 0;
        _minInferenceMs = long.MaxValue;
        _maxInferenceMs = 0;
        _totalActiveMs = 0;
        _latencyHistory.Clear();
        _p95LatencyMs = 0;
        _activeThresholdAlerts = [];

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

    private async Task OnPollingIntervalChangedAsync(ChangeEventArgs e)
    {
        if (!int.TryParse(e.Value?.ToString(), out var seconds) || seconds is < 5 or > 120) return;
        _livePollingSeconds = seconds;
        try { await JS.InvokeVoidAsync("PoWatchStorage.set", PollingStorageKey, seconds.ToString()); }
        catch { /* persistence is best-effort */ }
    }

    private async Task OnGpuPreferenceChangedAsync(ChangeEventArgs e)
    {
        var pref = e.Value?.ToString() ?? "default";
        if (pref == _selectedGpuPreference) return;
        _selectedGpuPreference = pref;
        await JS.InvokeVoidAsync("powatchInference.setPowerPreference", pref);
        NotificationService.Notify(NotificationSeverity.Info, "GPU", "Power preference updated. Restart monitoring to apply.", duration: 4000);
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

            var delaySeconds = Math.Max(1, _livePollingSeconds > 0 ? _livePollingSeconds : (observerState?.PollIntervalSeconds ?? FeatureFlags.Value.PollingIntervalSeconds));
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
        _totalCycles++;
        var _cycleStartTs = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            await RunCycleBodyAsync(cancellationToken);
        }
        finally
        {
            var elapsedMs = (long)System.Diagnostics.Stopwatch.GetElapsedTime(_cycleStartTs).TotalMilliseconds;
            _hudCycleMs = elapsedMs;
            _emaInferenceMs = _emaInferenceMs <= 0 ? elapsedMs : 0.8 * _emaInferenceMs + 0.2 * elapsedMs;
            _frameCount++;
            if (inferenceDiagnostics is not null)
            {
                if (!string.IsNullOrWhiteSpace(inferenceDiagnostics.ModelId)) _hudModel = inferenceDiagnostics.ModelId;
                if (!string.IsNullOrWhiteSpace(inferenceDiagnostics.Device)) _hudBackend = inferenceDiagnostics.Device.ToUpperInvariant();
                if (inferenceDiagnostics.LastInferenceMs is int inferMs && inferMs > 0)
                {
                    _totalActiveMs += inferMs;
                    if (inferMs < _minInferenceMs) _minInferenceMs = inferMs;
                    if (inferMs > _maxInferenceMs) _maxInferenceMs = inferMs;
                    _latencyHistory.Enqueue(inferMs);
                    if (_latencyHistory.Count > 100) _latencyHistory.Dequeue();
                    var sorted = _latencyHistory.OrderBy(x => x).ToArray();
                    _p95LatencyMs = sorted[(int)Math.Floor((sorted.Length - 1) * 0.95)];
                }
            }
        }
    }

    private async Task RunCycleBodyAsync(CancellationToken cancellationToken)
    {
        thinking = true;
        lastInferenceStatus = "Analysing frame...";
        await InvokeAsync(StateHasChanged);

        var inference = await JS.InvokeAsync<InferenceBridgeResult>(
            "powatchInference.captureAndInfer",
            cancellationToken,
            "Describe what you observe. Reply in this exact format:\nLABEL: <5 word activity> | NOTE: <one sentence describing the scene>\n\nExample: LABEL: Person seated using laptop | NOTE: Subject is working at a desk in a well-lit room.",
            liveCameraFeed,
            Math.Clamp(FeatureFlags.Value.MaxInferenceTokens, 32, 256));

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
                _skippedCycles++;
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

            _skippedCycles++;
            await RefreshDiagnosticsAsync();
            await InvokeAsync(StateHasChanged);
            return;
        }

        _structuredCycles++;
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
            await TryUploadEvidenceAsync(result.ImageReference, $"{result.SubjectDisplayName}: {inference.Activity}");
        }

        if (result is not null && !muted && !result.SkippedAsRedundant)
        {
            await AnnounceAsync(result.SubjectDisplayName, string.IsNullOrWhiteSpace(inference.SubjectHint));
        }

        // Accumulate threshold alerts for the banner
        if (result is not null && result.TriggeredAlerts.Count > 0 && FeatureFlags.Value.AlertThresholdsEnabled)
        {
            foreach (var alert in result.TriggeredAlerts)
            {
                if (!_activeThresholdAlerts.Any(a => a.RuleName == alert.RuleName && a.SubjectId == alert.SubjectId))
                    _activeThresholdAlerts.Add(alert);
            }
        }

        await RefreshTimelineAsync();
        await RefreshDiagnosticsAsync();
        await LoadSubjectsAsync();
        await InvokeAsync(StateHasChanged);
    }

    private void DismissThresholdAlert(ThresholdAlertDto alert) =>
        _activeThresholdAlerts.Remove(alert);

    private void DismissAllThresholdAlerts() =>
        _activeThresholdAlerts.Clear();

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
            await AnnounceAsync(result.SubjectDisplayName, true);
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
        try
        {
            observerState = await ApiClient.GetObserverStateAsync();
        }
        catch (HttpRequestException ex)
        {
            observerState ??= new ObserverRuntimeStateDto
            {
                ObservationLoopEnabled = false,
                SaveSignificantImages = false,
                DeveloperModeEnabled = false,
                PollIntervalSeconds = FeatureFlags.Value.PollingIntervalSeconds,
                CapturedAtUtc = DateTimeOffset.UtcNow,
                Status = "Unavailable",
                StatusDetail = "Observer state endpoint is temporarily unavailable."
            };

            NotificationService.Notify(
                NotificationSeverity.Warning,
                "Observer API",
                $"Could not load observer state. {ex.Message}",
                duration: 6000);
        }
    }

    private async Task RefreshDiagnosticsAsync()
    {
        try
        {
            inferenceDiagnostics = await JS.InvokeAsync<InferenceDiagnosticsSnapshot>("powatchInference.getInferenceDiagnostics");
            if (inferenceDiagnostics is not null)
            {
                _gpuAdapterVendor = inferenceDiagnostics.GpuAdapterVendor ?? string.Empty;
                _gpuAdapterName   = inferenceDiagnostics.GpuAdapterName   ?? "--";
                _hudMemory        = inferenceDiagnostics.JsHeapMb          ?? "--";
            }
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

}
