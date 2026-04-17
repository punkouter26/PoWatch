using Microsoft.JSInterop;

namespace PoWatch.Client.Services;

/// <summary>
/// Manages user preferences and local storage persistence.
/// </summary>
public class UserPreferencesService
{
    private readonly IJSRuntime _js;
    private UserPreferences _preferences = new();
    private bool _initialized;

    public UserPreferencesService(IJSRuntime js)
    {
        _js = js;
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;

        try
        {
            var stored = await _js.InvokeAsync<string?>("localStorage.getItem", "powatch:preferences");
            if (!string.IsNullOrEmpty(stored))
            {
                _preferences = System.Text.Json.JsonSerializer.Deserialize<UserPreferences>(stored) ?? new();
            }
        }
        catch
        {
            // Use defaults if localStorage unavailable
        }

        _initialized = true;
    }

    public UserPreferences Current => _preferences;

    public event Action<UserPreferences>? PreferencesChanged;

    public async Task UpdateAsync(Action<UserPreferences> update)
    {
        update(_preferences);
        await PersistAsync();
        PreferencesChanged?.Invoke(_preferences);
    }

    private async Task PersistAsync()
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(_preferences);
            await _js.InvokeVoidAsync("localStorage.setItem", "powatch:preferences", json);
        }
        catch
        {
            // Best effort persistence
        }
    }
}

public class UserPreferences
{
    public bool SidebarCollapsed { get; set; }
    public bool CompactMode { get; set; } = false;
    public string Theme { get; set; } = "dark";
    public string PreferredDateRange { get; set; } = "30";
    public List<string> ColumnSortPreferences { get; set; } = [];
    public bool SoundEnabled { get; set; } = true;
    public int AutoRefreshIntervalSeconds { get; set; } = 15;
    public List<string> PinnedPages { get; set; } = [];
}

/// <summary>
/// Connection status monitoring service.
/// </summary>
public class ConnectionStatusService
{
    private ConnectionStatus _status = ConnectionStatus.Connected;
    
    public ConnectionStatus Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                StatusChanged?.Invoke(value);
            }
        }
    }

    public event Action<ConnectionStatus>? StatusChanged;
}

public enum ConnectionStatus
{
    Connected,
    Disconnected,
    Reconnecting
}