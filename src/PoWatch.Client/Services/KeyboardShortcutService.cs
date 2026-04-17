using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace PoWatch.Client.Services;

/// <summary>
/// Global keyboard shortcut handler for power-user navigation.
/// </summary>
public class KeyboardShortcutService : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private DotNetObjectReference<KeyboardShortcutService>? _dotNetRef;
    private bool _isRegistered;

    public event Action<string>? ShortcutTriggered;

    public KeyboardShortcutService(IJSRuntime js)
    {
        _js = js;
    }

    public async Task InitializeAsync()
    {
        if (_isRegistered) return;
        
        _dotNetRef = DotNetObjectReference.Create(this);
        await _js.InvokeVoidAsync("powatchKeybindings.setup", _dotNetRef);
        _isRegistered = true;
    }

    [JSInvokable("OnShortcut")]
    public void OnShortcut(string shortcut)
    {
        ShortcutTriggered?.Invoke(shortcut);
    }

    public async ValueTask DisposeAsync()
    {
        if (_dotNetRef != null)
        {
            try
            {
                await _js.InvokeVoidAsync("powatchKeybindings.cleanup");
            }
            catch { /* Best effort cleanup */ }
            
            _dotNetRef.Dispose();
        }
    }
}

/// <summary>
/// Command palette service for quick navigation.
/// </summary>
public class CommandPaletteService
{
    private readonly NavigationManager _nav;
    private readonly KeyboardShortcutService _shortcutService;
    
    private readonly List<CommandItem> _commands =
    [
        new("Observer Hub", "/", "ctrl+1", "home"),
        new("Live Dashboard", "/live-dashboard", "ctrl+2", "chart"),
        new("Archives", "/archives", "ctrl+3", "archive"),
        new("Identity Nexus", "/identity", "ctrl+4", "users"),
        new("Diagnostics", "/diagnostics", "ctrl+5", "cpu"),
        new("Toggle Sidebar", "sidebar-toggle", "ctrl+b", "sidebar"),
        new("Refresh", "refresh", "r", "refresh"),
        new("Help", "help", "?", "help"),
    ];

    public bool IsOpen { get; private set; }
    public event Action<bool>? StateChanged;

    public CommandPaletteService(NavigationManager nav, KeyboardShortcutService shortcutService)
    {
        _nav = nav;
        _shortcutService = shortcutService;
    }

    public IReadOnlyList<CommandItem> GetCommands() => _commands;

    public IReadOnlyList<CommandItem> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return _commands;

        var lower = query.ToLowerInvariant();
        return _commands
            .Where(c => c.Label.Contains(lower, StringComparison.OrdinalIgnoreCase) ||
                       c.Shortcut.Contains(lower))
            .ToList();
    }

    public void Open()
    {
        IsOpen = true;
        StateChanged?.Invoke(true);
    }

    public void Close()
    {
        IsOpen = false;
        StateChanged?.Invoke(false);
    }

    public void Toggle()
    {
        if (IsOpen) Close(); else Open();
    }

    public void Execute(CommandItem command)
    {
        Close();

        switch (command.Action)
        {
            case var a when a.StartsWith("/"):
                _nav.NavigateTo(a);
                break;
            case "sidebar-toggle":
                // Handled by MainLayout
                break;
            case "refresh":
                // Trigger refresh event
                break;
            case "help":
                // Show help dialog
                break;
        }
    }
}

public record CommandItem(
    string Label,
    string Action,
    string Shortcut,
    string Icon)
{
    public string ShortcutDisplay => Shortcut.ToUpperInvariant().Replace("+", " + ");
}