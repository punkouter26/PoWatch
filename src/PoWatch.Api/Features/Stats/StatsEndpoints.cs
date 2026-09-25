using Microsoft.Extensions.Caching.Hybrid;
using PoWatch.Api.Security;
using PoWatch.Application.Services;

namespace PoWatch.Api.Features.Stats;

internal static class StatsEndpoints
{
    private static readonly HybridCacheEntryOptions Entry = new()
    {
        Expiration = TimeSpan.FromMinutes(5),
        LocalCacheExpiration = TimeSpan.FromMinutes(5)
    };

    /// <summary>Every cached stats entry for a user carries this tag; ingest evicts it.</summary>
    internal static string CacheTag(string userId) => $"stats:{userId}";

    internal static IEndpointRouteBuilder MapStatsFeature(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/stats").WithTags("Stats").RequireAuthorization();

        Map(group, "presence", (s, u, w, ct) => s.PresenceAsync(u, w, ct));
        Map(group, "space", (s, u, w, ct) => s.SpaceAsync(u, w, ct));
        Map(group, "objects", (s, u, w, ct) => s.ObjectsAsync(u, w, ct));
        Map(group, "patterns", (s, u, w, ct) => s.PatternsAsync(u, w, ct));
        Map(group, "environment", (s, u, w, ct) => s.EnvironmentAsync(u, w, ct));
        Map(group, "pipeline", (s, u, w, ct) => s.PipelineAsync(u, w, ct));

        return app;
    }

    private static void Map<T>(
        RouteGroupBuilder group,
        string family,
        Func<StatsQueryService, string, StatsWindow, CancellationToken, Task<T>> query)
    {
        group.MapGet($"/{family}", async (
                string? range,
                string? tz,
                Guid? sessionId,
                string? date,
                HttpContext http,
                StatsQueryService service,
                HybridCache cache,
                CancellationToken ct) =>
            {
                if (CurrentUser.Id(http.User) is not { } userId) return Results.Unauthorized();
                if (!StatsQueryService.TryParseRange(range, out var parsed))
                    return Results.BadRequest(new { message = "range must be one of session, today, 7d, 30d, all, day." });
                DateOnly? day = null;
                if (parsed == StatsRange.Day)
                {
                    if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedDay))
                        return Results.BadRequest(new { message = "range=day needs date=yyyy-MM-dd." });
                    day = parsedDay;
                }

                var window = await service.ResolveAsync(userId, parsed, tz, sessionId, ct, day);
                if (window is null) return Results.NotFound();

                // Windows ending "now" shift every call; key on the resolved grain bucket so repeat
                // polls within one bucket share an entry, and ingest clears the user's tag anyway.
                var key = $"stats:{userId}:{family}:{StatsQueryService.RangeName(parsed)}:{window.Zone.Id}:{sessionId}:{window.FromUtc.UtcTicks}";
                var result = await cache.GetOrCreateAsync(
                    key,
                    (service, userId, window, query),
                    static async (state, token) => await state.query(state.service, state.userId, state.window, token),
                    Entry,
                    [CacheTag(userId)],
                    ct);

                return Results.Ok(result);
            })
            .WithName($"Stats{char.ToUpperInvariant(family[0])}{family[1..]}")
            .WithSummary($"Stat family '{family}' for a range: session, today, 7d, 30d, all, or day (with date).");
    }
}
