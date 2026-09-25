using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using PoWatch.Shared.Models;

namespace PoWatch.Client.Services;

/// <summary>
/// Listens on the stats hub for "your stats changed" and raises <see cref="Changed"/>. Pages that
/// show stats subscribe and reload. If the hub cannot connect the pages simply keep their own
/// refresh timers, so push is an accelerator, never a dependency.
/// </summary>
public sealed class StatsFeed(NavigationManager navigation) : IAsyncDisposable
{
    private HubConnection? _connection;
    private Task? _connecting;

    public event Action<StatsChangedDto>? Changed;

    /// <summary>Achievements that just unlocked, for the header toast and the trophy cabinet.</summary>
    public event Action<AchievementsUnlockedDto>? Unlocked;

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    /// <summary>Connects once; later calls are no-ops. Safe to call from every page that shows stats.</summary>
    public Task EnsureConnectedAsync() => _connecting ??= ConnectAsync();

    private async Task ConnectAsync()
    {
        // The BFF cookie rides along on the same-origin WebSocket, so no token plumbing is needed.
        _connection = new HubConnectionBuilder()
            .WithUrl(navigation.ToAbsoluteUri("hubs/stats"))
            .WithAutomaticReconnect()
            .AddJsonProtocol(o => o.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, PoWatchJsonContext.Default))
            .Build();

        _connection.On<StatsChangedDto>("statsChanged", change => Changed?.Invoke(change));
        _connection.On<AchievementsUnlockedDto>("achievementsUnlocked", unlocked => Unlocked?.Invoke(unlocked));

        try
        {
            await _connection.StartAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or InvalidOperationException or System.Net.WebSockets.WebSocketException)
        {
            // Offline or hub unavailable: pages fall back to their refresh timers.
            _connecting = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null) await _connection.DisposeAsync();
    }
}
