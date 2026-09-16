using System.Globalization;
using Microsoft.AspNetCore.Components;
using PoWatch.Shared.Models;
using PoWatch.Shared.Services.Analytics;
using PoWatch.Client.Services;
using Radzen;

namespace PoWatch.Client.Pages;

/// <summary>Client heatmap derivation and energy-saving wake-on-motion state.</summary>
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

    // ─── Standby / wake-on-motion (#7) ────────────────────────────────────────────
    private readonly StandbyController _standby = new(idleThresholdSeconds: 900);
    private CancellationTokenSource? _standbyCts;
    private StandbyStatusDto? _standbyStatus;
    private bool _motionProbeActive;
    private bool _standbyEnabled = true;

    /// <summary>Whether the inference loop is currently running normally (vs paused in standby).</summary>
    public bool StandbyActive => _standby.Mode == "standby";

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

}

/// <summary>JSON shape for the JS motion probe response (matches inference-bridge.js probeMotion).</summary>
public sealed record MotionProbeResult(bool Ok, double Diff, int Score, string Level, string? Error);
