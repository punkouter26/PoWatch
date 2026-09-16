using Microsoft.AspNetCore.Components;

namespace PoWatch.Client.Services;

/// <summary>
/// A self-disposing polling loop for client-side pages that need to refresh themselves
/// every N seconds (Health, Diagnostics, SubjectGlanceGrid, etc).
/// <para>
/// Replaces three near-identical inline loops that each kept their own
/// <see cref="CancellationTokenSource"/> and varied in subtle ways: one leaked its CTS on
/// every toggle, another never disposed on navigation away, a third forgot the
/// <see cref="OperationCanceledException"/> filter. Centralising the loop also makes the
/// dispose semantics obvious — call <see cref="DisposeAsync"/> once, when the host page
/// is itself disposed.
/// </para>
/// </summary>
public sealed class PollLoop : IAsyncDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _runner;
    private TimeSpan _interval;

    /// <summary>True while a tick is currently executing. Pages can use this to debounce
    /// rapid user-triggered refreshes.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Start (or restart) the loop. Calling Start twice replaces the previous loop
    /// cleanly so callers don't have to track the existing one.</summary>
    /// <param name="interval">How often to run the action. Clamped to ≥1 second.</param>
    /// <param name="action">The work to perform. Runs once immediately, then every interval.
    /// If it throws, the loop logs to the console and keeps going — a transient 503 from
    /// a storage dependency should not silently kill the dashboard.</param>
    /// <param name="invokeAsync">A Blazor InvokeAsync equivalent — usually
    /// <c>InvokeAsync(StateHasChanged)</c>. The tick runs on a thread-pool thread; the UI
    /// repaint must happen on the renderer.</param>
    public void Start(TimeSpan interval, Func<CancellationToken, Task> action, Func<Func<Task>, Task> invokeAsync)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(interval, TimeSpan.FromSeconds(1));
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(invokeAsync);

        Stop();
        _interval = interval;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _runner = Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    IsRunning = true;
                    try
                    {
                        await RunTickAsync(action, invokeAsync, token);
                    }
                    finally
                    {
                        IsRunning = false;
                    }

                    if (token.IsCancellationRequested) break;
                    await Task.Delay(_interval, token);
                }
            }
            catch (OperationCanceledException) { /* expected on Stop */ }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[PollLoop] tick failed: {ex.GetType().Name}: {ex.Message}");
            }
        }, token);
    }

    private static async Task RunTickAsync(
        Func<CancellationToken, Task> action,
        Func<Func<Task>, Task> invokeAsync,
        CancellationToken token)
    {
        if (token.IsCancellationRequested) return;

        // Run the action on its own thread (it may already have invoked off-renderer),
        // then schedule the UI repaint through the supplied InvokeAsync so the renderer
        // owns the StateHasChanged call.
        try
        {
            await action(token);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            // Swallow per-tick exceptions; the loop survives so transient errors
            // (a 503 from a storage dependency, a brief network blip) don't permanently
            // disable the dashboard. The page is responsible for showing its own banner.
        }

        // Always repaint, even on error, so the timestamp / "loading" UI stays fresh.
        if (!token.IsCancellationRequested)
        {
            try { await invokeAsync(() => Task.CompletedTask); }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[PollLoop] repaint failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>Stop and dispose the loop. Safe to call multiple times.</summary>
    public void Stop()
    {
        if (_cts is null) return;
        try { _cts.Cancel(); } catch { /* already disposed */ }
        _cts.Dispose();
        _cts = null;
    }

    public async ValueTask DisposeAsync()
    {
        Stop();
        if (_runner is not null)
        {
            try { await _runner.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[PollLoop] dispose wait failed: {ex.GetType().Name}: {ex.Message}");
            }
            _runner = null;
        }
    }
}