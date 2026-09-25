using PoWatch.Application.Services;
using PoWatch.Shared.Models;

namespace PoWatch.Unit;

public sealed class TemplateRecapTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 24, 13, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo UtcMinus5 =
        TimeZoneInfo.CreateCustomTimeZone("Test/UTC-5", TimeSpan.FromHours(-5), "UTC-5", "UTC-5");

    [Fact]
    public Task A_busy_afternoon_reads_the_same_every_time()
    {
        var facts = new RecapFacts(
            "Session #AB12",
            "Thu 24 Sep, 08:00 · 3 hours",
            T0,
            T0.AddHours(3),
            UtcMinus5,
            new PresenceStatsDto
            {
                SecondsObserved = 3 * 3600 + 12 * 60,
                Occupancy = 0.42,
                Visits = 17,
                VisitsPerHour = 5.3,
                PeakConcurrency = 3,
                LongestEmptySeconds = 41 * 60,
                LongestStillSeconds = 14 * 60,
                BusiestStartUtc = T0.AddMinutes(95)
            },
            new ObjectStatsDto
            {
                Classes = [new() { Name = "person" }, new() { Name = "cup" }, new() { Name = "cat" }, new() { Name = "laptop" }, new() { Name = "chair" }, new() { Name = "dog" }],
                Rarest = "dog",
                Regulars = [new() { Id = "r1", DisplayName = "Bob", Class = "person", Visits = 6, DwellSeconds = 2400 }]
            },
            new EnvironmentStatsDto
            {
                CaptionCount = 24,
                Switches = [new() { AtUtc = T0.AddHours(2), On = true }],
                Words = [new() { Word = "desk", Count = 9 }, new() { Word = "laptop", Count = 7 }, new() { Word = "cat", Count = 4 }],
                WeirdestCaption = "A cat supervises a laptop."
            },
            new PatternStatsDto
            {
                Anomalies = [new() { Metric = "visits/hour", Today = 5.3, Mean = 2.1, StdDev = 1.2, Z = 2.7, Flagged = true }]
            },
            [new MomentDto { AtUtc = T0.AddMinutes(20), Text = "First cat of the session", Score = 0.8 }]);

        return Verify(TemplateRecap.Build(facts));
    }
}
