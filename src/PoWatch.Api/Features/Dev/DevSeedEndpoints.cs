using Bogus;
using Microsoft.Extensions.Caching.Hybrid;
using PoWatch.Api.Features.Stats;
using PoWatch.Api.Security;
using PoWatch.Application.Contracts;
using PoWatch.Domain.Models;
using PoWatch.Domain.Services;

namespace PoWatch.Api.Features.Dev;

/// <summary>
/// Development and Test only: fills the caller's rollups with a believable synthetic history so the
/// dashboards have something to show and the stats queries can be timed against a year of data.
/// </summary>
internal static class DevSeedEndpoints
{
    private const int MaxDays = 400;
    private const int HourlyDays = 30;

    internal static IEndpointRouteBuilder MapDevSeedFeature(this IEndpointRouteBuilder app, IWebHostEnvironment environment)
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Test"))
            return app;

        app.MapPost("/api/dev/seed", async (
                int? days,
                string? tz,
                HttpContext http,
                IRollupStore rollups,
                HybridCache cache,
                TimeProvider time,
                CancellationToken ct) =>
            {
                if (CurrentUser.Id(http.User) is not { } userId) return Results.Unauthorized();
                // Rollups merge, so a second seed would stack another copy of history on top (a 15-hour
                // day reading 149 h observed). UI tests seed the shared guest on every run: seed once.
                if ((await rollups.GetAllTimeAsync(userId, ct)).Ticks > 0)
                    return Results.Ok(new { days = 0, buckets = 0 });

                var dayCount = Math.Clamp(days ?? 30, 1, MaxDays);
                var zone = !string.IsNullOrWhiteSpace(tz) && TimeZoneInfo.TryFindSystemTimeZoneById(tz, out var found) ? found : TimeZoneInfo.Utc;

                var plan = BuildHistory(dayCount, zone, time.GetUtcNow());
                var writes = plan.Buckets.Select(b => (Func<Task>)(() => rollups.MergeAsync(userId, b.Grain, b.StartUtc, b.Rollup, ct)));
                foreach (var chunk in writes.Chunk(32))
                    await Task.WhenAll(chunk.Select(write => write()));
                await rollups.MergeAsync(userId, RollupGrain.AllTime, DateTimeOffset.UnixEpoch, plan.AllTime, ct);

                await cache.RemoveByTagAsync(StatsEndpoints.CacheTag(userId), ct);
                return Results.Ok(new { days = dayCount, buckets = plan.Buckets.Count });
            })
            .WithTags("Dev")
            .RequireAuthorization()
            .WithName("DevSeed")
            .WithSummary("Development/Test only: seed synthetic rollup history for the caller.");

        return app;
    }

    private sealed record SeedBucket(RollupGrain Grain, DateTimeOffset StartUtc, Rollup Rollup);

    private sealed record SeedPlan(List<SeedBucket> Buckets, Rollup AllTime);

    /// <summary>
    /// A deterministic, diurnal history: quiet nights, busy afternoons, a cat that wanders through.
    /// Day buckets for every day, hour buckets for the last month, minute buckets for today so far.
    /// </summary>
    private static SeedPlan BuildHistory(int days, TimeZoneInfo zone, DateTimeOffset nowUtc)
    {
        var faker = new Faker { Random = new Randomizer(42) };
        var today = LocalDay.Of(nowUtc, zone);
        var buckets = new List<SeedBucket>();
        var allTime = Rollup.Empty;

        for (var d = days - 1; d >= 0; d--)
        {
            var date = today.AddDays(-d);
            var dayStart = LocalDay.Window(date, zone).StartUtc;
            var day = Rollup.Empty;

            for (var hour = 0; hour < 24; hour++)
            {
                var hourStart = dayStart.AddHours(hour);
                if (hourStart >= nowUtc) break;

                var hourRollup = SyntheticHour(faker, hourStart, hour);
                day = Rollup.Merge(day, hourRollup);
                if (d < HourlyDays) buckets.Add(new SeedBucket(RollupGrain.Hour, hourStart, hourRollup));

                if (d == 0)
                {
                    for (var minute = 0; minute < 60 && hourStart.AddMinutes(minute) < nowUtc; minute++)
                    {
                        var minuteStart = hourStart.AddMinutes(minute);
                        buckets.Add(new SeedBucket(RollupGrain.Minute, minuteStart, SyntheticSlice(faker, minuteStart, hour, 6)));
                    }
                }
            }

            buckets.Add(new SeedBucket(RollupGrain.Day, dayStart, day with { BucketStartUtc = dayStart }));
            allTime = Rollup.Merge(allTime, day);
        }

        return new SeedPlan(buckets, allTime with { BucketStartUtc = DateTimeOffset.UnixEpoch });
    }

    private static Rollup SyntheticHour(Faker faker, DateTimeOffset startUtc, int localHour) =>
        SyntheticSlice(faker, startUtc, localHour, 360);

    private static Rollup SyntheticSlice(Faker faker, DateTimeOffset startUtc, int localHour, int ticks)
    {
        var busy = localHour is >= 8 and < 23;
        var people = busy ? faker.Random.Int(0, 3) : faker.Random.Int(0, 1) * faker.Random.Int(0, 1);
        var cat = faker.Random.Bool(0.2f) ? 1 : 0;
        var motion = busy ? faker.Random.Double(0.02, 0.3) : faker.Random.Double(0, 0.02);
        var grid = Enumerable.Range(0, SpatialGrid.Cells).Select(_ => faker.Random.Float(0, busy ? 0.4f : 0.05f)).ToArray();
        var classes = new Dictionary<string, ClassCount> { ["person"] = new(people, people), ["cup"] = new(1, 1) };
        if (cat > 0) classes["cat"] = new(1, 1);

        var slice = Rollup.FromTick(new Tick
        {
            SessionId = Guid.Empty,
            StartUtc = startUtc,
            PixelSamples = 40,
            DetectorSamples = 10,
            MotionMean = motion,
            MotionMax = Math.Min(1, motion * 2),
            LuminanceMean = localHour is >= 7 and < 20 ? faker.Random.Double(0.5, 0.8) : faker.Random.Double(0.02, 0.2),
            Palette = [faker.Random.Int(0, 0xFFFFFF)],
            MotionGrid = grid,
            PresenceGrid = grid,
            Classes = classes
        });

        var occupied = people + cat > 0;
        return slice with
        {
            Ticks = ticks,
            Seconds = ticks * Tick.MaxDurationSeconds,
            PixelSamples = ticks * 40L,
            DetectorSamples = ticks * 10L,
            OccupiedTicks = occupied ? ticks : 0,
            Visits = busy ? faker.Random.Int(0, ticks / 60 + 1) : 0
        };
    }
}
