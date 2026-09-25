using BenchmarkDotNet.Attributes;
using PoWatch.Application.Services;
using PoWatch.Domain.Models;
using PoWatch.Domain.Services;
using PoWatch.Infrastructure.Persistence;

namespace PoWatch.Benchmarks;

/// <summary>
/// The hot paths behind every stats page: folding a day of minute rollups, summarising it, and a
/// full "last 30 days" presence query over hourly rollups (what the Stats page asks for on load).
/// </summary>
[MemoryDiagnoser]
public class RollupBenchmarks
{
    private const string User = "bench";
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private Rollup[] _day = [];
    private StatsQueryService _stats = null!;
    private StatsWindow _month = null!;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        var random = new Random(42);
        _day = Enumerable.Range(0, 1440).Select(m => Rollup.FromTick(Tick(Now.AddDays(-1).AddMinutes(m), random))).ToArray();

        var rollups = new InMemoryRollupStore();
        for (var hour = 0; hour < 30 * 24; hour++)
        {
            var at = Now.AddHours(-hour);
            var bucket = Enumerable.Range(0, 360).Select(i => Rollup.FromTick(Tick(at.AddSeconds(i * 10), random))).Aggregate(Rollup.Merge);
            await rollups.MergeAsync(User, RollupGrain.Hour, RollupBuckets.StartUtc(RollupGrain.Hour, at, TimeZoneInfo.Utc), bucket, CancellationToken.None);
        }

        _stats = new StatsQueryService(rollups, new InMemorySensingLog(), new InMemorySessionRepository(), new InMemoryRegularStore(), new FixedTime(Now));
        _month = (await _stats.ResolveAsync(User, StatsRange.Month, "UTC", null, CancellationToken.None))!;
    }

    [Benchmark]
    public Rollup MergeADayOfMinutes() => _day.Aggregate(Rollup.Merge);

    [Benchmark]
    public PresenceSummary SummariseADayOfMinutes() => PresenceStats.Summarize(_day, TimeSpan.FromMinutes(1));

    [Benchmark]
    public Task<PoWatch.Shared.Models.PresenceStatsDto> PresenceForThirtyDays() => _stats.PresenceAsync(User, _month, CancellationToken.None);

    private static Tick Tick(DateTimeOffset at, Random random)
    {
        var people = random.Next(0, 3);
        return new Tick
        {
            SessionId = Guid.Empty,
            StartUtc = at,
            PixelSamples = 40,
            DetectorSamples = 10,
            MotionMean = random.NextDouble() * 0.2,
            MotionMax = random.NextDouble() * 0.5,
            LuminanceMean = 0.3 + random.NextDouble() * 0.4,
            Palette = [random.Next(0, 0xFFFFFF), random.Next(0, 0xFFFFFF)],
            MotionGrid = Enumerable.Range(0, SpatialGrid.Cells).Select(_ => (float)random.NextDouble()).ToArray(),
            Classes = people > 0
                ? new Dictionary<string, ClassCount> { ["person"] = new(people, people) }
                : new Dictionary<string, ClassCount>()
        };
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
