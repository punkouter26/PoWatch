namespace PoWatch.Client.Services;

/// <summary>
/// Singleton that holds the global monitoring on/off state so NavMenu and ObserverHub stay in sync.
/// </summary>
public sealed class MonitorStateService
{
    private bool _isRunning;

    public bool IsRunning => _isRunning;

    /// <summary>Fired on any state change so subscribers can call StateHasChanged.</summary>
    public event EventHandler? StateChanged;

    public void SetRunning(bool running)
    {
        if (_isRunning == running) return;
        _isRunning = running;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
