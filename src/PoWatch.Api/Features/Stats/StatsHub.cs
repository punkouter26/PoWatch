using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PoWatch.Api.Security;
using PoWatch.Shared.Models;

namespace PoWatch.Api.Features.Stats;

/// <summary>
/// Pushes "your stats changed" to every open tab of the user after an ingest lands, so the stats
/// page and the stats wall refresh straight away instead of polling. Server-to-client only.
/// </summary>
[Authorize]
internal sealed class StatsHub : Hub
{
    public const string Path = "/hubs/stats";
    public const string ChangedMethod = "statsChanged";
}

/// <summary>Addresses SignalR users by the same id their data is partitioned by.</summary>
internal sealed class CurrentUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) => CurrentUser.Id(connection.User);
}

internal static class StatsHubExtensions
{
    public static Task NotifyStatsChangedAsync(this IHubContext<StatsHub> hub, string userId, StatsChangedDto change, CancellationToken ct) =>
        hub.Clients.User(userId).SendAsync(StatsHub.ChangedMethod, change, ct);
}
