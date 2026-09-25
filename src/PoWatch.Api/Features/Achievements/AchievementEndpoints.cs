using Microsoft.AspNetCore.SignalR;
using PoWatch.Api.Features.Stats;
using PoWatch.Api.Security;
using PoWatch.Application.Services;
using PoWatch.Shared.Models;

namespace PoWatch.Api.Features.Achievements;

internal static class AchievementEndpoints
{
    public const string UnlockedMethod = "achievementsUnlocked";

    internal static IEndpointRouteBuilder MapAchievementsFeature(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/achievements", async (HttpContext http, AchievementService achievements, CancellationToken ct) =>
                CurrentUser.Id(http.User) is { } userId
                    ? Results.Ok(await achievements.CabinetAsync(userId, ct))
                    : Results.Unauthorized())
            .WithTags("Achievements")
            .RequireAuthorization()
            .WithName("TrophyCabinet")
            .WithSummary("Every achievement (locked and unlocked) and every personal record.");

        return app;
    }

    /// <summary>Checks for newly earned achievements and, if any, tells every open tab so it can toast them.</summary>
    internal static async Task EvaluateAndAnnounceAsync(
        this AchievementService achievements, IHubContext<StatsHub> hub, string userId, Guid sessionId, CancellationToken ct)
    {
        var unlocked = await achievements.EvaluateAsync(userId, sessionId, ct);
        if (unlocked.Count > 0)
            await hub.Clients.User(userId).SendAsync(UnlockedMethod, new AchievementsUnlockedDto { Unlocked = [.. unlocked] }, ct);
    }
}
