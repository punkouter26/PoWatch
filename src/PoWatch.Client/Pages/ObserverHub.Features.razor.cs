using System.Globalization;
using Microsoft.AspNetCore.Components;
using PoWatch.Shared.Models;
using PoWatch.Shared.Services.Analytics;
using PoWatch.Client.Services;
using Radzen;

namespace PoWatch.Client.Pages;

/// <summary>
/// Wiring for the four opt-in analytics features layered onto the Live Room:
/// (1) <see cref="DailyActivityHeatmapBuilder"/> builds the 24-hour density strip.
/// (2) <see cref="NotificationBundler"/> collapses rapid-fire alerts into a single toast.
/// (3) <see cref="StandbyController"/> gates the inference loop behind a cheap motion probe.
/// (4) <see cref="PatternComparator"/> summarises today vs the rolling baseline on the People page.
/// </summary>
/// <remarks>
/// All four pure-logic classes live in <c>PoWatch.Shared.Services.Analytics</c> so the
/// Unit test suite can exercise them without referencing the Blazor client. This partial
/// holds only the Blazor glue (parameter marshalling, JS-Interop calls, lifecycle).
/// </remarks>
/// <remarks>
/// All four are pure C# classes exercised directly by unit tests; this partial holds only the
/// Blazor glue (parameter marshalling, JS-Interop calls, lifecycle).
/// </remarks>
public partial class ObserverHub
{
    // ─── Heatmap (#2) ────────────────────────────────────────────────────────────
    // Seeded with TODAY at declaration time. A default(DailyActivityBucketsDto) has
    // Date == 01/01/0001, so if OnInitializedAsync ever aborts before the first RebuildHeatmap()
    // (e.g. one of the startup refreshes faults), the strip used to be labelled "Mon 1 Jan" —
    // a wrong date is worse than an empty one, and this seed makes that state unreachable.
    private DailyActivityBucketsDto _heatmapBuckets =
        DailyActivityHeatmapBuilder.Build([], DateOnly.FromDateTime(DateTime.Now));
    private int _heatmapDisplayCap = 6;
    private string HeatmapTitle => _heatmapBuckets.Date.ToString("ddd d MMM", CultureInfo.CurrentCulture);
    private string HeatmapMeta => _heatmapBuckets.TotalEvents == 0
        ? "No activity yet today"
        : $"{_heatmapBuckets.TotalEvents} events · peak hour {_peakHeatmapHour}";

    private int _peakHeatmapHour;
    private int PeakHeatmapHour => _peakHeatmapHour;

    private void RebuildHeatmap()
    {
        _heatmapBuckets = DailyActivityHeatmapBuilder.Build(streamItems, DateOnly.FromDateTime(DateTime.Now));
        var max = 0;
        var peakHour = 0;
        for (var h = 0; h < 24; h++)
        {
            var total = _heatmapBuckets.Routine[h] + _heatmapBuckets.Notable[h] + _heatmapBuckets.Urgent[h];
            if (total > max)
            {
                max = total;
                peakHour = h;
            }
        }
        _peakHeatmapHour = peakHour;
        // Adapt the cap so a single spike does not flatten the strip; otherwise leave the default.
        if (max > _heatmapDisplayCap) _heatmapDisplayCap = max;
    }

    // ─── Notification bundling (#6) ───────────────────────────────────────────────
    private readonly NotificationBundler _alertBundler = new(windowSeconds: 120, maxPending: 16);
    private readonly List<BundledAlertDto> _renderedBundles = [];
    private const int MaxRenderedBundles = 4;

    /// <summary>Push a raw ingest verdict through the bundler; emit any bundles that aged out.</summary>
    private void PushBundledAlert(IngestObservationResultDto result)
    {
        if (result is null || result.Dropped || result.SkippedAsRedundant) return;

        var ready = _alertBundler.Push(result);
        if (ready.Count == 0) return;

        _renderedBundles.InsertRange(0, ready);
        // Cap the rendered list — too many at once defeats the bundling.
        if (_renderedBundles.Count > MaxRenderedBundles)
            _renderedBundles.RemoveRange(MaxRenderedBundles, _renderedBundles.Count - MaxRenderedBundles);

        // Each bundle becomes a Radzen toast — but only the headline is shown so we don't
        // re-create the alert fatigue that the bundler was supposed to remove.
        foreach (var bundle in ready)
        {
            var severity = bundle.Severity switch
            {
                "urgent" => NotificationSeverity.Error,
                "notable" => NotificationSeverity.Warning,
                _ => NotificationSeverity.Info
            };
            var title = bundle.Severity switch
            {
                "urgent" => "Unusual activity",
                "notable" => "Notable moments",
                _ => "Routine"
            };
            NotificationService.Notify(severity, title, bundle.Headline, duration: 5000);
        }
    }

    private void DismissBundledAlert(BundledAlertDto bundle) =>
        _renderedBundles.Remove(bundle);

    // ─── Standby / wake-on-motion (#7) ────────────────────────────────────────────
    private readonly StandbyController _standby = new(idleThresholdSeconds: 900);
    private CancellationTokenSource? _standbyCts;
    private StandbyStatusDto? _standbyStatus;
    private bool _motionProbeActive;
    private bool _standbyEnabled = true;

    /// <summary>Whether the inference loop is currently running normally (vs paused in standby).</summary>
    public bool StandbyActive => _standby.Mode == "standby";

    private string StandbyChipLabel => _standby.Mode == "standby"
        ? $"Standby · {(int)_standby.SecondsSinceMotion():F0}s still"
        : _standby.Mode == "awake" && _standby.SecondsSinceMotion() > 60
            ? $"Awake · {(int)_standby.SecondsSinceMotion() / 60}m still"
            : "Awake";

    private void StartStandbyWatchdog()
    {
        _standby.Enabled = _standbyEnabled;
        _standbyCts?.Cancel();
        _standbyCts = new CancellationTokenSource();
        var token = _standbyCts.Token;
        _ = Task.Run(() => RunStandbyWatchdogAsync(token));
    }

    private async Task RunStandbyWatchdogAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var nextMode = _standby.Tick();
                if (nextMode != _standby.Mode || _standbyStatus is null)
                {
                    _standbyStatus = _standby.Snapshot(_motionProbeActive);
                    await InvokeAsync(StateHasChanged);

                    if (nextMode == "standby")
                    {
                        // Drop the model — biggest single saving. Webcam stays open for the probe.
                        await JS.TryInvokeVoidAsync("powatchInference.unloadModel");
                    }
                }

                // Probe the camera cheaply only when in standby; awake mode already runs the
                // full frame-diff as part of captureAndInfer so a second probe would be wasted.
                if (_standby.Mode == "standby")
                {
                    var probe = await JS.TryInvokeAsync<MotionProbeResult>("powatchInference.probeMotion", liveCameraFeed);
                    if (probe is { Ok: true } && (probe.Diff >= 0.06 || probe.Level == "High" || probe.Level == "Medium"))
                    {
                        _standby.RecordMotion();
                        _standbyStatus = _standby.Snapshot(_motionProbeActive);
                        await InvokeAsync(StateHasChanged);
                        // Wake the model for the next observation cycle.
                        if (monitoring && monitorCts is { IsCancellationRequested: false })
                        {
                            // Touch a no-op so the worker re-loads on next captureAndInfer; the
                            // worker auto-reloads when _model is null and inference is requested.
                        }
                    }
                    _motionProbeActive = true;
                }
                else
                {
                    _motionProbeActive = false;
                }

                try { await Task.Delay(TimeSpan.FromSeconds(2), token); }
                catch (OperationCanceledException) { break; }
            }
        }
        catch (OperationCanceledException) { /* expected on Stop */ }
        catch
        {
            // Standby is opt-in convenience — a broken watchdog must not bring the monitor loop down.
        }
    }

    private void StopStandbyWatchdog()
    {
        _standbyCts?.Cancel();
        _standbyCts?.Dispose();
        _standbyCts = null;
        _motionProbeActive = false;
    }

    /// <summary>Refresh the standby snapshot for the chip — called every state change.</summary>
    private void RefreshStandbyStatus()
    {
        _standbyStatus = _standby.Snapshot(_motionProbeActive);
    }

    // ─── Pattern comparison (#8) ───────────────────────────────────────────────────
    private PatternComparisonDto? _patternComparison;

    private async Task RefreshPatternComparisonAsync(string subjectId)
    {
        if (string.IsNullOrWhiteSpace(subjectId)) return;
        try
        {
            var baseline = await ApiClient.GetSubjectBaselineAsync(subjectId, days: 7);
            if (baseline is not null)
            {
                _patternComparison = PatternComparator.Compare(baseline);
            }
        }
        catch
        {
            // Non-critical — the pattern panel just keeps its previous state.
        }
    }
}

/// <summary>JSON shape for the JS motion probe response (matches inference-bridge.js probeMotion).</summary>
public sealed record MotionProbeResult(bool Ok, double Diff, int Score, string Level, string? Error);
