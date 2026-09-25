using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using PoWatch.Client.Services;
using PoWatch.Shared.Models;
using Radzen;

namespace PoWatch.Client.Pages;

/// <summary>Camera lifecycle and the observation cycle, including cancellation and server verdicts.</summary>
public partial class ObserverHub
{
    private async Task StartMonitoringAsync()
    {
        // TTV: re-verify only the operator disable flag before starting — a cheap guardrail.
        // The timeline no longer needs a pre-start round-trip; it refreshes after the first cycle.
        await RefreshStateAsync();

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

        // Cancel any in-flight inference from a prior session so it can't finish and overwrite
        // the new run's state. (See SafeJsInterop.cs note about CancellationToken serialization.)
        await JS.TryInvokeVoidAsync("powatchInference.cancelInFlight");

        monitoringStartedAtUtc = DateTimeOffset.UtcNow;
        lastSyncAtUtc = null;
        lastSyncStatus = "Connecting...";
        lastInferenceStatus = "Starting preview...";
        lastWasNotable = false;
        lastDetectedSubject = "Awaiting first detection";
        lastConfidencePercent = 0;
        lastConfidenceLabel = "Awaiting AI";

        // Reset inference analytics counters
        _totalCycles = 0;
        _skippedCycles = 0;
        _structuredCycles = 0;
        _minInferenceMs = long.MaxValue;
        _maxInferenceMs = 0;
        _latencyHistory.Clear();
        _p95LatencyMs = 0;

        // Fresh session — clear any latched pipeline-degradation warning from a prior run.
        _pipelineHealthLatched = false;
        _fp16WarningLatched = false;

        monitoring = true;
        await InvokeAsync(StateHasChanged);

        var previewStatus = await JS.TryInvokeAsync<string>("powatchInference.startPreview", liveCameraFeed) ?? "Bridge unavailable";
        if (!string.Equals(previewStatus, "OK", StringComparison.OrdinalIgnoreCase))
        {
            monitoring = false;
            // Fix #2 + #8: persist the failure so the operator can see WHY watching didn't start.
            // Toast-only feedback was being missed on a kiosk — the page silently snapped back to
            // Standby with no breadcrumb, so the operator thought the system was monitoring when
            // it wasn't.
            _lastAttemptFailure = string.IsNullOrWhiteSpace(previewStatus) ? "Camera preview did not start." : previewStatus;
            _lastAttemptAtUtc = DateTimeOffset.UtcNow;
            lastSyncStatus = "Camera unavailable";
            lastInferenceStatus = previewStatus;
            NotificationService.Notify(NotificationSeverity.Warning, "Camera", previewStatus, duration: 6000);
            await RefreshDiagnosticsAsync();
            await InvokeAsync(StateHasChanged);
            return;
        }

        // Clear any pinned failure once we're genuinely live.
        _lastAttemptFailure = null;
        _lastAttemptAtUtc = null;

        await RefreshDiagnosticsAsync();
        await PlayCueAsync("start");

        _ = Task.Run(() => RunMonitorLoopAsync(monitorCts.Token));
        // No per-second heartbeat here: the running clock is owned by the isolated
        // <LiveDurationTimer> component, so the whole telemetry grid no longer re-renders every second.

        // Standby watchdog (#7): only after Start, because an operator who has not yet hit Start
        // should not see "Standby · waiting for motion" — they should see "Ready to watch".
        StartStandbyWatchdog();
    }

    private async Task StopMonitoringAsync()
    {
        await PlayCueAsync("stop");
        monitoring = false;
        thinking = false;
        lastSyncStatus = "Paused";
        lastInferenceStatus = "Stopped";
        // Operator intentionally stopped — wipe the failure latch so we don't keep nagging.
        _lastAttemptFailure = null;
        _lastAttemptAtUtc = null;

        monitorCts?.Cancel();
        // Cancel any in-flight captureAndInfer call so we don't get a stale inference round-trip
        // finishing after Stop. The C# CancellationToken can't cross JSInterop (IntPtr on the
        // token's WaitHandle trips JSON serialization), so the bridge keeps its own AbortController.
        await JS.TryInvokeVoidAsync("powatchInference.cancelInFlight");

        await JS.TryInvokeVoidAsync("powatchInference.stopMonitor");
        await RefreshDiagnosticsAsync();

        StopStandbyWatchdog();
        RefreshStandbyStatus();

        await InvokeAsync(StateHasChanged);
    }

    private async Task OnModelSwitched(object _)
    {
        // Safe wrapper — model selection persists in the worker; bridge absence is harmless.
        await JS.TryInvokeVoidAsync("powatchInference.setModel", selectedModelKey);
        NotificationService.Notify(NotificationSeverity.Info, "Model", "Will load on next inference.", duration: 3000);
    }

    private async Task OnPollingIntervalChangedAsync(ChangeEventArgs e)
    {
        if (!int.TryParse(e.Value?.ToString(), out var seconds) || seconds is < 5 or > 120) return;
        _livePollingSeconds = seconds;
        try { await UserPrefs.SetAsync(PollingStorageKey, seconds.ToString(CultureInfo.InvariantCulture)); }
        catch { /* persistence is best-effort */ }
    }

    private async Task OnGpuPreferenceChangedAsync(ChangeEventArgs e)
    {
        var pref = e.Value?.ToString() ?? "default";
        if (pref == _selectedGpuPreference) return;
        _selectedGpuPreference = pref;
        // Safe wrapper — GPU preference applies on next worker spin-up.
        await JS.TryInvokeVoidAsync("powatchInference.setPowerPreference", pref);
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
                LatchPipelineHealth("error", $"Inference pipeline error — {ex.Message}");
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
            _emaInferenceMs = _emaInferenceMs <= 0 ? elapsedMs : 0.8 * _emaInferenceMs + 0.2 * elapsedMs;
            if (inferenceDiagnostics is not null)
            {
                if (inferenceDiagnostics.LastInferenceMs is int inferMs && inferMs > 0)
                {
                    if (inferMs < _minInferenceMs) _minInferenceMs = inferMs;
                    if (inferMs > _maxInferenceMs) _maxInferenceMs = inferMs;
                    _latencyHistory.Enqueue(inferMs);
                    if (_latencyHistory.Count > 100) _latencyHistory.Dequeue();
                    // Reuse a fixed buffer + in-place sort instead of allocating a LINQ
                    // OrderBy().ToArray() every cycle just to read one percentile.
                    var n = _latencyHistory.Count;
                    _latencyHistory.CopyTo(_p95Buffer, 0);
                    Array.Sort(_p95Buffer, 0, n);
                    _p95LatencyMs = _p95Buffer[(int)Math.Floor((n - 1) * 0.95)];
                }
            }
        }
    }

    private async Task RunCycleBodyAsync(CancellationToken cancellationToken)
    {
        thinking = true;
        lastInferenceStatus = "Analysing frame...";
        await InvokeAsync(StateHasChanged);

        // Safe wrapper with a sentinel fallback — captureAndInfer is the hot path but a missing
        // bridge (or a hung worker) must surface as a clean "unavailable" result, not a
        // JSException that takes the whole Blazor tree down with the global error overlay.
        // NOTE: do NOT pass cancellationToken as a JSInterop argument. System.Text.Json tries
        // to serialize every arg, and CancellationToken.WatchHandle.Handle is IntPtr — that
        // surfaces as the SerializeTypeInstanceNotSupported toast. Cancel from the C# side
        // via JS.TryInvokeVoidAsync("powatchInference.cancelInFlight") instead.
        var inference = await JS.TryInvokeAsync<InferenceBridgeResult>(
            "powatchInference.captureAndInfer",
            ObservationPrompt,
            liveCameraFeed,
            Math.Clamp(FeatureFlags.Value.MaxInferenceTokens, 16, 96))
            ?? new InferenceBridgeResult
            {
                IsAvailable = false,
                Status = "Inference bridge unavailable",
                Activity = "Unknown",
                ConfidenceLabel = "Bridge missing"
            };

        lastInferenceStatus = inference.Status;

        if (!inference.IsAvailable)
        {
            thinking = false;
            lastConfidencePercent = 0;
            lastConfidenceLabel = "Awaiting AI";

            // Silent skips — don't show as errors, preserve current status.
            // Match on "is this a model-output outcome" rather than on specific prefixes: keying off
            // the "Low-quality" prefix meant a newly-added status ("Model returned an empty response")
            // fell through to the generic handler below and reported "Sync blocked", hiding the real
            // reason again. Anything the worker classifies is reported verbatim.
            var isFrameSkip = inference.Status.StartsWith("Frame unchanged", StringComparison.OrdinalIgnoreCase);
            var isModelOutputSkip =
                inference.Status.StartsWith("Low-quality", StringComparison.OrdinalIgnoreCase) ||
                inference.Status.StartsWith("Model returned an empty response", StringComparison.OrdinalIgnoreCase) ||
                inference.Status.StartsWith("Inference error:", StringComparison.OrdinalIgnoreCase);

            if (isFrameSkip || isModelOutputSkip)
            {
                // "Awaiting clearer frame" used to be shown here, which blames the camera for what is
                // really a model-output rejection — a run skipping 100% of cycles looked like lighting.
                lastSyncStatus = isFrameSkip ? "No sync needed" : inference.Status;
                if (isModelOutputSkip)
                {
                    lastRejectedOutput = inference.RawOutput;
                    lastGenerationDiagnostic = inference.GenerationDiagnostic;
                }

                _skippedCycles++;
                await RefreshDiagnosticsAsync();
                await InvokeAsync(StateHasChanged);
                return;
            }

            if (string.Equals(inference.Status, "Webcam unavailable in this browser. Fallback preview active.", StringComparison.OrdinalIgnoreCase))
            {
                monitoring = false;
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

            lastWasNotable = result.IsSignificant;

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
            // Reconnect state: surface a server/auth dropout on the kiosk instead of failing silently.
            LatchPipelineHealth("warn", "Reconnecting to server — last sync did not complete.");
        }

        if (result is not null && !result.Dropped && !result.SkippedAsRedundant && result.IsSignificant)
        {
            await TryUploadEvidenceAsync(result.ImageReference, inference.CapturedImageDataUrl, $"{result.SubjectDisplayName}: {inference.Activity}");
        }

        if (result is not null && result.IsSignificant && !result.IsOutlier && !muted && !result.SkippedAsRedundant)
        {
            await JS.TryInvokeVoidAsync("powatchAudio.announce",
                $"Notable activity: {result.SignificantReason ?? result.Detail}");
        }

        // Standby (#7): if the camera frame changed enough to count as "motion", reset the idle timer
        // so the watchdog does not park the model on a busy room.
        if (inference.MotionScore >= 6) _standby.RecordMotion();

        // Diagnostics is a cheap local JS call — always refresh it for the HUD.
        await RefreshDiagnosticsAsync();

        // Only pay for the timeline + subjects round-trips when this cycle actually persisted
        // something new. Redundant/dropped cycles change nothing on the server, so re-fetching
        // is pure waste — especially on a kiosk polling every few seconds.
        if (result is not null && !result.Dropped && !result.SkippedAsRedundant)
        {
            await RefreshTimelineAsync();
            await LoadSubjectsAsync();
            // Heatmap (#2) is a pure derivation off the timeline we just refreshed; rebuild now
            // so the strip never lags the activity panel by more than one cycle.
            RebuildHeatmap();
        }

        RefreshStandbyStatus();
        await InvokeAsync(StateHasChanged);
    }
}
