using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using PoWatch.Client.Services;
using PoWatch.Shared.Models;
using Radzen;

namespace PoWatch.Client.Pages;

public partial class ObserverHub
{
    protected override async Task OnInitializedAsync()
    {
        muted = true;

        // TTV: these loads are independent (state, timeline, local JS diagnostics, subjects, model list).
        // Fetch them concurrently instead of serially so the first paint lands after the slowest
        // round-trip, not the sum of all of them.
        // Each refresh already degrades gracefully (try/catch + banner) for HttpRequestException, but
        // ANY other fault used to bubble out of Task.WhenAll and abort the rest of this method —
        // which skipped RebuildHeatmap()/RefreshStandbyStatus() and left the hero state half-wired.
        // SafeAsync keeps one bad refresh from taking down initialization.
        await Task.WhenAll(
            SafeAsync(RefreshStateAsync, "state"),
            SafeAsync(RefreshTimelineAsync, "timeline"),
            SafeAsync(RefreshDiagnosticsAsync, "diagnostics"),
            SafeAsync(LoadSubjectsAsync, "subjects"),
            SafeAsync(LoadModelRegistryAsync, "model registry"));

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

        // Build the heatmap from the timeline we just loaded. The strip is a pure derivation,
        // so it is rebuilt every time the timeline refreshes rather than polling the API again.
        RebuildHeatmap();
        RefreshStandbyStatus();
        // Start the standby watchdog only if monitoring is already running; otherwise the
        // Start button flips it on (see StartMonitoringAsync). Either path keeps the chip in sync.
        if (monitoring) StartStandbyWatchdog();
    }

    // Load the model list from the shared /model-registry.json (single source of truth shared with the
    // inference worker, rule 1.5). Trim-safe via the source-generated context. On failure the picker is
    // simply empty — inference still runs on the default selectedModelKey, which the worker resolves from
    // the same file.
    /// <summary>
    /// Runs one startup refresh, isolating any fault so Task.WhenAll cannot abort
    /// OnInitializedAsync before the heatmap and standby state are wired.
    /// </summary>
    private static async Task SafeAsync(Func<Task> refresh, string name)
    {
        try
        {
            await refresh();
        }
        catch (Exception ex)
        {
            // Non-fatal: the page renders with whatever state it has; per-refresh paths that
            // matter already latch the reconnect banner.
            Console.Error.WriteLine($"[PoWatch] Startup refresh '{name}' failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task LoadModelRegistryAsync()
    {
        try
        {
            var entries = await ModelRegistry.GetAsync();
            ModelOptions = entries.Select(e => new ModelOption(e.Key, e.Label)).ToList();
            // Registry order IS preference order — best-performing model first. Startup always
            // takes it rather than a key compiled into this file, so trimming or reordering
            // model-registry.json moves the default with it and the default can never dangle at
            // a model that was deleted. The session's own choice is not persisted, so there is
            // no operator selection to override here.
            selectedModelKey = ModelOptions[0].Value;
            return;
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
        RebuildHeatmap();
        _standby.RecordMotion();
        RefreshStandbyStatus();
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

            ShowUrgentAlert(result, "Unknown movement", null);
            await PlayCueAsync("alert");
        }

        if (result is not null && !muted)
        {
            await AnnounceAsync(result.SubjectDisplayName, true);
        }

        await RefreshTimelineAsync();
        RebuildHeatmap();
        _standby.RecordMotion();
        RefreshStandbyStatus();
    }

    // Lightweight, oscillator-synthesized interaction cue (audit #8). Best-effort: audio must never
    // break the monitoring flow, and it only works after a user gesture has unlocked the AudioContext.
    private async Task PlayCueAsync(string kind)
    {
        if (muted) return;
        // Safe wrapper — audio bridge is optional; absence must not throw.
        await JS.TryInvokeVoidAsync("powatchAudio.cue", kind);
    }

    private async Task AnnounceAsync(string subjectDisplayName, bool isUnknown)
    {
        if (isUnknown)
        {
            await JS.TryInvokeVoidAsync("powatchAudio.playChirp");
        }

        subjectDisplayName = DisplayText.SubjectName(subjectDisplayName, !isUnknown);
        var message = isUnknown
            ? $"New subject detected: {subjectDisplayName}"
            : $"Subject identified: {subjectDisplayName}";

        await JS.TryInvokeVoidAsync("powatchAudio.announce", message);
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
                    await JS.TryInvokeVoidAsync("powatchBlobUpload.uploadFrame", access.SasUrl, capturedImageDataUrl);
                }
                else
                {
                    // Fallback for dev-tool injected events that have no real frame
                    await JS.TryInvokeVoidAsync("powatchBlobUpload.uploadSvgPlaceholder", access.SasUrl, label);
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
            inferenceDiagnostics = await JS.TryInvokeAsync<InferenceDiagnosticsSnapshot>("powatchInference.getInferenceDiagnostics");
            if (inferenceDiagnostics is not null)
            {
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
        try
        {
            // The caregiver's LOCAL calendar day — the same convention the Archives date picker,
            // the heatmap builder, and the server's ShiftClock all use. Requesting the UTC day
            // shifted the Live Room timeline by the UTC offset: at UTC-5 the evening's events
            // landed on "tomorrow" and only appeared after midnight.
            var chapter = await ApiClient.GetChapterAsync(DateOnly.FromDateTime(DateTime.Now), NarrativeMode.Prose);
            streamItems = chapter?.Timeline is not null
                ? chapter.Timeline.OrderByDescending(x => x.ObservedAtUtc).Take(50).ToList()
                : [];
        }
        catch (HttpRequestException)
        {
            // A failed timeline load must not take down the whole Live Room (this runs unguarded
            // inside OnInitializedAsync's Task.WhenAll). Keep whatever we had and latch the banner,
            // matching RefreshStateAsync's degradation path.
            LatchPipelineHealth("warn", "Could not load today's activity timeline — will retry on the next refresh.");
        }
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
        monitorCts?.Dispose();
        monitorCts = null;
        _keepAliveCts?.Cancel();
        _keepAliveCts?.Dispose();
        _keepAliveCts = null;

        // Safe wrapper — a missing inference-bridge must not throw during disposal.
        await JS.TryInvokeVoidAsync("powatchInference.stopMonitor");

        GC.SuppressFinalize(this);
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

        /// <summary>
        /// The model's verbatim reply, populated when a quality gate rejected it. Without this the
        /// operator sees a rising skip count with no way to learn what the model actually said.
        /// </summary>
        public string? RawOutput { get; init; }

        /// <summary>
        /// Tensor shapes and a full-sequence decode preview, populated only when generation came
        /// back empty. Distinguishes "the model emitted nothing" from "we sliced the tokens wrong".
        /// </summary>
        public string? GenerationDiagnostic { get; init; }
    }

}
