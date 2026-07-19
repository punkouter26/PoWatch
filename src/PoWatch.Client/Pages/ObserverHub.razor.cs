using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using PoWatch.Client.Services;
using PoWatch.Shared.Models;
using Radzen;
using System.Net.Http;
using System.Net.Http.Json;

namespace PoWatch.Client.Pages;

public partial class ObserverHub
{
    protected override async Task OnInitializedAsync()
    {
        muted = true;

        // Honour a ?subjectFilter= drill-down from the Live Dashboard subject cards.
        if (!string.IsNullOrWhiteSpace(SubjectFilterQuery))
        {
            _subjectFilter = SubjectFilterQuery;
        }

        // TTV: these loads are independent (state, timeline, local JS diagnostics, subjects, model list).
        // Fetch them concurrently instead of serially so the first paint lands after the slowest
        // round-trip, not the sum of all of them.
        await Task.WhenAll(
            RefreshStateAsync(),
            RefreshTimelineAsync(),
            RefreshDiagnosticsAsync(),
            LoadSubjectsAsync(),
            LoadModelRegistryAsync());

        // Restore persisted polling interval; fall back to appSettings default.
        // Uses UserPreferencesService (typed localStorage wrapper) so this works
        // across full reloads and survives the model + model-registry migrations
        // to a typed service in step #8 of the modernization plan.
        try
        {
            var stored = await UserPrefs.GetAsync(PollingStorageKey);
            _livePollingSeconds = int.TryParse(stored, out var parsed) && parsed is >= 5 and <= 120
                ? parsed
                : FeatureFlags.Value.PollingIntervalSeconds;
        }
        catch
        {
            _livePollingSeconds = FeatureFlags.Value.PollingIntervalSeconds;
        }

        // Kiosk keep-alive: an unattended-but-powered display slides its auth cookie only when it
        // talks to the API. While monitoring, the per-cycle ingest already does that; when idle,
        // this low-frequency ping keeps the sliding session warm so the wall display never lapses.
        _keepAliveCts = new CancellationTokenSource();
        _ = Task.Run(() => RunSessionKeepAliveAsync(_keepAliveCts.Token));
    }

    // Load the model list from the shared /model-registry.json (single source of truth shared with the
    // inference worker, rule 1.5). Trim-safe via the source-generated context. On failure the picker is
    // simply empty — inference still runs on the default selectedModelKey, which the worker resolves from
    // the same file.
    private async Task LoadModelRegistryAsync()
    {
        try
        {
            var entries = await Http.GetFromJsonAsync("model-registry.json", PoWatchJsonContext.Default.ModelRegistryEntryArray);
            if (entries is { Length: > 0 })
            {
                ModelOptions = entries.Select(e => new ModelOption(e.Key, e.Label)).ToList();
                if (!ModelOptions.Any(o => o.Value == selectedModelKey))
                    selectedModelKey = ModelOptions[0].Value;
                return;
            }

            // 200 but empty/malformed — surface it rather than silently blanking the picker.
            Console.Error.WriteLine("[PoWatch] model-registry.json loaded but contained no entries.");
        }
        catch (Exception ex)
        {
            // A swallowed failure here is exactly why the model picker can render blank with no clue why.
            // Log the real cause to the browser console and tell the operator, instead of failing silently.
            Console.Error.WriteLine($"[PoWatch] Failed to load model-registry.json: {ex.GetType().Name}: {ex.Message}");
            NotificationService.Notify(
                NotificationSeverity.Warning,
                "Model list",
                "Could not load the model registry; falling back to the default model. See the browser console for details.",
                duration: 6000);
        }

        // Fail safe: never leave the picker empty. The inference worker resolves the full model set from the
        // same file, so a single fallback option keyed on the current selection keeps the UI usable.
        if (ModelOptions.Count == 0)
            ModelOptions = [new ModelOption(selectedModelKey, selectedModelKey)];
    }

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

        monitoringStartedAtUtc = DateTimeOffset.UtcNow;
        lastSyncAtUtc = null;
        lastSyncStatus = "Connecting...";
        lastInferenceStatus = "Starting preview...";
        lastAlertLevel = AlertLevel.Normal;
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

        // Fresh session — clear any latched pipeline-degradation warning from a prior run.
        _pipelineHealthLatched = false;
        _fp16WarningLatched = false;

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
        await PlayCueAsync("start");

        _ = Task.Run(() => RunMonitorLoopAsync(monitorCts.Token));
        // No per-second heartbeat here: the running clock is owned by the isolated
        // <LiveDurationTimer> component, so the whole telemetry grid no longer re-renders every second.
    }

    private async Task StopMonitoringAsync()
    {
        await PlayCueAsync("stop");
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
        try { await UserPrefs.SetAsync(PollingStorageKey, seconds.ToString()); }
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

        var inference = await JS.InvokeAsync<InferenceBridgeResult>(
            "powatchInference.captureAndInfer",
            cancellationToken,
            "Describe what you observe. Reply in this exact format:\nLABEL: <activity> | NOTE: <one sentence describing the scene>\n\nExample: LABEL: Person seated using laptop | NOTE: Subject is working at a desk in a well-lit room.",
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
                lastAlertLevel = AlertLevel.Urgent;
                lastAlertReason = result.Detail;

                // Full-screen takeover (Win #4): the one moment that matters must be unmissable.
                _alertOverlaySubject = result.SubjectDisplayName;
                _alertOverlaySubjectId = result.SubjectId;
                _alertOverlayReason = string.IsNullOrWhiteSpace(result.Detail)
                    ? (inference.SignificantReason ?? "Unusual activity detected in the room.")
                    : result.Detail;
                _alertOverlayActivity = inference.Activity;
                _alertOverlayTime = DateTimeOffset.Now.ToString("HH:mm:ss");
                _alertOverlayImage = inference.CapturedImageDataUrl;
                _alertOverlayVisible = true;
                await PlayCueAsync("alert");
            }
            else if (inference.IsSignificant)
            {
                lastAlertLevel = AlertLevel.Watch;
                lastAlertReason = result.Detail;
            }
            else
            {
                lastAlertLevel = AlertLevel.Normal;
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
            // Reconnect state: surface a server/auth dropout on the kiosk instead of failing silently.
            LatchPipelineHealth("warn", "Reconnecting to server — last sync did not complete.");
        }

        if (result is not null && !result.Dropped && !result.SkippedAsRedundant && inference.IsSignificant)
        {
            await TryUploadEvidenceAsync(result.ImageReference, inference.CapturedImageDataUrl, $"{result.SubjectDisplayName}: {inference.Activity}");
        }

        if (result is not null && !muted && !result.SkippedAsRedundant)
        {
            await AnnounceAsync(result.SubjectDisplayName, string.IsNullOrWhiteSpace(inference.SubjectHint));
        }

        // Accumulate threshold alerts for the banner
        if (result is not null && result.TriggeredAlerts.Count > 0 && FeatureFlags.Value.AlertThresholdsEnabled)
        {
            var hadAlerts = _activeThresholdAlerts.Count;
            foreach (var alert in result.TriggeredAlerts)
            {
                if (!_activeThresholdAlerts.Any(a => a.RuleName == alert.RuleName && a.SubjectId == alert.SubjectId))
                    _activeThresholdAlerts.Add(alert);
            }
            // Micro-cue on a newly-raised threshold alert (audit #8).
            if (_activeThresholdAlerts.Count > hadAlerts) await PlayCueAsync("alert");
        }

        // Diagnostics is a cheap local JS call — always refresh it for the HUD.
        await RefreshDiagnosticsAsync();

        // Only pay for the timeline + subjects round-trips when this cycle actually persisted
        // something new. Redundant/dropped cycles change nothing on the server, so re-fetching
        // is pure waste — especially on a kiosk polling every few seconds.
        if (result is not null && !result.Dropped && !result.SkippedAsRedundant)
        {
            await RefreshTimelineAsync();
            await LoadSubjectsAsync();
        }

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
            await TryUploadEvidenceAsync(result.ImageReference, null, $"{result.SubjectDisplayName}: Desk Work");
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
            await TryUploadEvidenceAsync(result.ImageReference, null, $"{result.SubjectDisplayName}: Clinical outlier");

            // Drive the full-screen takeover from the dev-tool injector too, so the alert flow is testable.
            lastAlertLevel = AlertLevel.Urgent;
            lastAlertReason = result.Detail;
            _alertOverlaySubject = result.SubjectDisplayName;
            _alertOverlaySubjectId = result.SubjectId;
            _alertOverlayReason = string.IsNullOrWhiteSpace(result.Detail) ? "Unusual activity detected in the room." : result.Detail;
            _alertOverlayActivity = "Unknown movement";
            _alertOverlayTime = DateTimeOffset.Now.ToString("HH:mm:ss");
            _alertOverlayImage = null;
            _alertOverlayVisible = true;
            await PlayCueAsync("alert");
        }

        if (result is not null && !muted)
        {
            await AnnounceAsync(result.SubjectDisplayName, true);
        }

        await RefreshTimelineAsync();
    }

    // Lightweight, oscillator-synthesized interaction cue (audit #8). Best-effort: audio must never
    // break the monitoring flow, and it only works after a user gesture has unlocked the AudioContext.
    private async Task PlayCueAsync(string kind)
    {
        try { await JS.InvokeVoidAsync("powatchAudio.cue", kind); }
        catch { /* audio unavailable — ignore */ }
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

    private async Task TryUploadEvidenceAsync(string? imageReference, string? capturedImageDataUrl, string label)
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
                if (!string.IsNullOrWhiteSpace(capturedImageDataUrl))
                {
                    // Upload the actual webcam frame captured at inference time
                    await JS.InvokeVoidAsync("powatchBlobUpload.uploadFrame", access.SasUrl, capturedImageDataUrl);
                }
                else
                {
                    // Fallback for dev-tool injected events that have no real frame
                    await JS.InvokeVoidAsync("powatchBlobUpload.uploadSvgPlaceholder", access.SasUrl, label);
                }
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

            LatchPipelineHealth("warn", "Reconnecting to server — the API is temporarily unreachable.");

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
                _gpuAdapterName = inferenceDiagnostics.GpuAdapterName ?? "--";
                _hudMemory = inferenceDiagnostics.JsHeapMb ?? "--";

                // GPU degraded to fp32 — not fatal, but the operator should know throughput will drop.
                // Latch once so it doesn't re-fire every cycle.
                if (inferenceDiagnostics.Fp16FallbackUsed && !_fp16WarningLatched)
                {
                    _fp16WarningLatched = true;
                    LatchPipelineHealth("warn", "GPU running fp32 fallback — fp16 unsupported on this adapter (slower).");
                }
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

    private async Task RunSessionKeepAliveAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
                // A lightweight authenticated GET slides the BFF cookie so an idle kiosk
                // session never lapses. Failures latch the reconnect banner (see RefreshStateAsync).
                await RefreshStateAsync();
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Ignore expected shutdown.
        }
    }

    public async ValueTask DisposeAsync()
    {
        monitorCts?.Cancel();
        _keepAliveCts?.Cancel();

        await JS.InvokeVoidAsync("powatchInference.stopMonitor");
    }

    // ── WebGL shader bridge: tell the canvas overlay when the inference loop
    // enters/leaves its "thinking" phase. Diff-gated so we only fire on actual
    // transitions (avoids JS-interop per frame).
    private bool _lastWebcamShellThinking;
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            // Bind the canvas overlay on first paint — the JS side auto-attaches
            // any .webcam-shell elements via MutationObserver, but the initial
            // bootstrap hits immediately when the script loads.
            await JS.InvokeVoidAsync("powatchWebcamShell.setThinking", false);
        }
        if (thinking != _lastWebcamShellThinking)
        {
            _lastWebcamShellThinking = thinking;
            try { await JS.InvokeVoidAsync("powatchWebcamShell.setThinking", thinking); }
            catch { /* script not present */ }
        }
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
        public string? CapturedImageDataUrl { get; init; }
    }

}
