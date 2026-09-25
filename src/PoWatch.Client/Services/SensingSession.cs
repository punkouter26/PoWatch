using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using PoWatch.Shared.Models;
using PoWatch.Shared.Services.Sensing;
using PoWatch.Shared.Services.Tracking;

namespace PoWatch.Client.Services;

/// <summary>
/// Runs one observation session in the browser: the pixel layer, the detector and the vision model
/// (or, in demo mode, a scripted <see cref="SyntheticScene"/>) feed the tracker and the tick batcher,
/// and closed batches are posted to the server in order, kept in the outbox until acknowledged.
/// The Live page renders <see cref="Live"/> and listens to <see cref="Changed"/>.
/// </summary>
public sealed class SensingSession(PoWatchApiClient api, IJSRuntime js, TimeProvider time) : IAsyncDisposable
{
    private static readonly PoWatchJsonContext Json = PoWatchJsonContext.Default;
    private static readonly TimeSpan FlushEvery = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DemoStep = TimeSpan.FromMilliseconds(250);

    private readonly CentroidTracker _tracker = new();
    private TickBatcher _batcher = new(time);
    private VlmScheduler _vlm = new(time);
    private SyntheticScene? _scene;
    private DotNetObjectReference<SensingSession>? _self;
    private ElementReference? _video;
    private CancellationTokenSource? _cts;
    private bool _vlmBusy;
    private bool _vlmEnabled;
    private int _demoStep;

    public LiveSensingState Live { get; private set; } = new();

    public bool IsRunning => _cts is not null;

    /// <summary>Raised whenever <see cref="Live"/> changes; handlers should marshal to the renderer.</summary>
    public event Action? Changed;

    /// <summary>Starts a server session and the sensing loops. Returns an error message, or null on success.</summary>
    public async Task<string?> StartAsync(ElementReference? video, bool demo, bool enableVlm, string timeZoneId)
    {
        if (IsRunning) return null;

        if (!demo)
        {
            if (video is null) return "No video element to watch.";
            var preview = await js.TryInvokeAsync<string>("powatchInference.startPreview", video);
            if (preview != "OK") return preview ?? "The camera could not be started.";
        }

        var session = await api.StartSessionAsync(new StartSessionRequestDto { SessionId = Guid.NewGuid(), TimeZoneId = timeZoneId });
        if (session is null) return "The server did not start a session.";

        _video = video;
        _vlmEnabled = enableVlm && !demo;
        _batcher = new TickBatcher(time);
        _vlm = new VlmScheduler(time);
        _scene = demo ? new SyntheticScene() : null;
        _demoStep = 0;
        _cts = new CancellationTokenSource();
        Live = new LiveSensingState { Session = session, Demo = demo, StartedUtc = session.StartedUtc };

        if (!demo)
        {
            _self = DotNetObjectReference.Create(this);
            await js.TryInvokeVoidAsync("powatchPixels.start", video, _self, 250);
            _ = LoadDetectorAsync(video!.Value, _cts.Token);
        }

        _ = RunLoopAsync(FlushEvery, FlushAsync, _cts.Token);
        _ = RunLoopAsync(demo ? DemoStep : TimeSpan.FromSeconds(1), demo ? DemoTickAsync : VlmTickAsync, _cts.Token);
        Notify();
        return null;
    }

    public async Task StopAsync()
    {
        if (_cts is null) return;
        await _cts.CancelAsync();
        _cts.Dispose();
        _cts = null;

        await js.TryInvokeVoidAsync("powatchPixels.stop");
        await js.TryInvokeVoidAsync("powatchDetector.stop");
        if (_scene is null) await js.TryInvokeVoidAsync("powatchInference.stopMonitor");

        _batcher.Flush(final: true);
        await SendPendingAsync(CancellationToken.None);
        if (Live.Session is { } session)
            Live.Session = await api.StopSessionAsync(session.Id) ?? session;

        _self?.Dispose();
        _self = null;
        Notify();
    }

    [JSInvokable]
    public void OnPixelSample(string json)
    {
        if (!IsRunning || JsonSerializer.Deserialize(json, Json.PixelSamplePayload) is not { } p) return;
        AddPixel(new PixelSample(p.AtUtc, p.Motion, p.Luminance, p.Grid, p.Palette));
    }

    [JSInvokable]
    public void OnDetections(string json)
    {
        if (!IsRunning || JsonSerializer.Deserialize(json, Json.DetectionsPayload) is not { } p) return;
        Live.DetectorLatencyMs = p.LatencyMs;
        AddDetections(p.AtUtc, p.Detections.Select(d => new Detection(d.Label, d.Score, d.X0, d.Y0, d.X1, d.Y1)).ToList());
    }

    [JSInvokable]
    public void OnDetectorError(string message)
    {
        Live.LastError = $"Detector: {message}";
        Notify();
    }

    private void AddPixel(PixelSample sample)
    {
        _batcher.AddPixel(sample);
        _vlm.ObserveMotion(sample.Motion);
        Live.RecordPixel(sample, time.GetUtcNow());
        Notify();
    }

    private void AddDetections(DateTimeOffset atUtc, IReadOnlyList<Detection> detections)
    {
        var frame = _tracker.Update(atUtc, detections);
        _batcher.AddDetections(atUtc, frame);
        Live.RecordDetections(frame, time.GetUtcNow());
        Notify();
    }

    private void AddCaption(DateTimeOffset atUtc, string raw)
    {
        if (CaptionParser.Parse(raw) is not { } caption) return;
        _batcher.AddCaption(atUtc, caption.Text, caption.Notable ? 1 : null);
        Live.RecordCaption(caption, atUtc);
        Notify();
    }

    private Task DemoTickAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var withCaption = _vlm.IsDue;
        var frame = _scene!.Next(now, withCaption);
        AddPixel(frame.Pixels);
        if (_demoStep++ % 4 == 0) AddDetections(now, frame.Detections);
        if (frame.Caption is { } caption)
        {
            _vlm.MarkRun();
            AddCaption(now, caption);
        }

        Live.DetectorModel ??= "synthetic scene";
        return Task.CompletedTask;
    }

    private async Task VlmTickAsync(CancellationToken ct)
    {
        if (!_vlmEnabled || _vlmBusy || !_vlm.IsDue || _video is null) return;
        _vlmBusy = true;
        try
        {
            _vlm.MarkRun();
            var result = await js.TryInvokeAsync<VlmResultPayload>("powatchInference.captureAndInfer", CaptionParser.Prompt, _video, 48);
            if (result is { IsAvailable: true })
                AddCaption(time.GetUtcNow(), string.IsNullOrWhiteSpace(result.ClinicalPayload) ? result.Activity : result.ClinicalPayload);
            else if (result is not null)
                Live.VlmStatus = result.Status;
        }
        finally
        {
            _vlmBusy = false;
        }
    }

    private async Task LoadDetectorAsync(ElementReference video, CancellationToken ct)
    {
        var info = await js.TryInvokeAsync<DetectorInfoPayload>("powatchDetector.load");
        if (ct.IsCancellationRequested) return;
        if (info is null)
        {
            Live.LastError = "The object detector could not be loaded; pixel stats continue.";
            Notify();
            return;
        }

        Live.DetectorModel = info.ModelId;
        Live.DetectorDevice = info.Device;
        await js.TryInvokeVoidAsync("powatchDetector.start", video, _self, 1000, 0.5);
        Notify();
    }

    private async Task FlushAsync(CancellationToken ct)
    {
        _batcher.Flush();
        await SendPendingAsync(ct);
    }

    private async Task SendPendingAsync(CancellationToken ct)
    {
        if (Live.Session is not { } session) return;
        foreach (var batch in _batcher.Pending.ToList())
        {
            try
            {
                var result = await api.PostBatchAsync(session.Id, batch, ct);
                // Null means the server refused it outright; retrying would never help.
                if (result is null) Live.RejectedBatches++;
                else Live.BatchesSent++;
                _batcher.Acknowledge(batch.BatchKey);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                Live.LastError = "Offline — batches are queued and will be resent.";
                break;
            }
        }

        Live.PendingBatches = _batcher.Pending.Count;
        Live.LostTicks = _batcher.LostTicks;
        Notify();
    }

    private static async Task RunLoopAsync(TimeSpan period, Func<CancellationToken, Task> step, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(period);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                await step(ct);
        }
        catch (OperationCanceledException)
        {
            // Session stopped.
        }
    }

    private void Notify() => Changed?.Invoke();

    public async ValueTask DisposeAsync()
    {
        if (IsRunning) await StopAsync();
        _self?.Dispose();
    }
}
