namespace PoWatch.Client.Services;

/// <summary>
/// Which side drawer is open: at most one at a time (opening Settings closes the bell and back), and
/// any navigation closes it (MainLayout).
/// </summary>
public sealed class DrawerState
{
    public const string Settings = "settings";
    public const string Tray = "tray";

    public string? Open { get; private set; }

    public event Action? Changed;

    public bool IsOpen(string name) => Open == name;

    public void Show(string name) => Set(name);

    public void Toggle(string name) => Set(Open == name ? null : name);

    public void Close() => Set(null);

    private void Set(string? name)
    {
        if (Open == name) return;
        Open = name;
        Changed?.Invoke();
    }
}
