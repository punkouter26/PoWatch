using System.Threading.RateLimiting;
using PoWatch.Api.Security;

namespace PoWatch.Api.Hosting;

internal static class RateLimiting
{
    /// <summary>
    /// Rate limiting on the API, sign-in, hub and shared-recap routes: 300 requests a minute per signed-in
    /// user (per IP when anonymous). A running session posts a batch every 10 s and polls stats, so 60
    /// would trip. Static assets are exempt (a cold load is ~200 files); the Test host is exempt (suites
    /// share one guest).
    /// </summary>
    public static IServiceCollection AddPoWatchRateLimiting(this IServiceCollection services, IWebHostEnvironment env) =>
        services.AddRateLimiter(rl =>
        {
            rl.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            rl.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
            {
                var path = http.Request.Path;
                // "/s" is the public shared-recap page: anonymous, so it is limited per IP like sign-in is.
                var limited = path.StartsWithSegments("/api") || path.StartsWithSegments("/auth") || path.StartsWithSegments("/hubs") || path.StartsWithSegments("/s");
                if (!limited || env.IsEnvironment("Test"))
                    return RateLimitPartition.GetNoLimiter(string.Empty);

                var key = CurrentUser.Id(http.User) ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = 300,
                    Window = TimeSpan.FromMinutes(1),
                    SegmentsPerWindow = 6
                });
            });
        });
}
