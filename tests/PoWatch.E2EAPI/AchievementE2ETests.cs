using System.Net.Http.Json;
using PoWatch.Shared.Models;

namespace PoWatch.E2EAPI;

public sealed class AchievementE2ETests(ApiE2EFactory factory) : IClassFixture<ApiE2EFactory>
{
    [Fact]
    public async Task Watching_unlocks_trophies_once_and_sets_records()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Fake-User", $"trophy-{Guid.NewGuid():N}");
        using var stranger = factory.CreateClient();
        stranger.DefaultRequestHeaders.Add("X-Fake-User", $"nobody-{Guid.NewGuid():N}");

        var session = (await (await client.PostAsJsonAsync("/api/sessions", new StartSessionRequestDto { TimeZoneId = "UTC" }))
            .Content.ReadFromJsonAsync<SessionDto>())!;

        // Starting a session is enough for First Light.
        var afterStart = (await client.GetFromJsonAsync<TrophyCabinetDto>("/api/achievements"))!;
        Assert.True(afterStart.Achievements.Count >= 15);
        Assert.True(afterStart.Achievements.Single(a => a.Id == "first-light").Unlocked);
        Assert.False(afterStart.Achievements.Single(a => a.Id == "first-cat").Unlocked);

        var at = session.StartedUtc.AddSeconds(1);
        var batch = new IngestBatchDto
        {
            BatchKey = Guid.NewGuid(),
            Ticks = [new() { StartUtc = at, PixelSamples = 40, MotionMean = 0.2, MotionMax = 0.4, LuminanceMean = 0.5,
                             Classes = new() { ["person"] = new() { Max = 2, Mean = 2 }, ["cat"] = new() { Max = 1, Mean = 1 } } }],
            Events = [new() { AtUtc = at, Kind = "TrackEnter", TrackId = "T1", Class = "cat", Edge = "Left" }]
        };
        (await client.PostAsJsonAsync($"/api/sessions/{session.Id}/batches", batch)).EnsureSuccessStatusCode();

        var cabinet = (await client.GetFromJsonAsync<TrophyCabinetDto>("/api/achievements"))!;
        var kitty = cabinet.Achievements.Single(a => a.Id == "first-cat");
        Assert.True(kitty.Unlocked);
        Assert.Contains(cabinet.Records, r => r is { Id: "peak-concurrency", Value: 3 });
        Assert.Contains(cabinet.Records, r => r is { Id: "busiest-day-visits", Value: 1 });
        Assert.Contains(cabinet.Records, r => r is { Id: "daily-streak", Value: 1, Display: "1 day" });

        // A later batch neither re-unlocks nor moves the first unlock time.
        (await client.PostAsJsonAsync($"/api/sessions/{session.Id}/batches",
            new IngestBatchDto { BatchKey = Guid.NewGuid(), Ticks = batch.Ticks, Events = batch.Events })).EnsureSuccessStatusCode();
        var again = (await client.GetFromJsonAsync<TrophyCabinetDto>("/api/achievements"))!;
        Assert.Equal(kitty.UnlockedAtUtc, again.Achievements.Single(a => a.Id == "first-cat").UnlockedAtUtc);

        var theirs = (await stranger.GetFromJsonAsync<TrophyCabinetDto>("/api/achievements"))!;
        Assert.DoesNotContain(theirs.Achievements, a => a.Unlocked);
        Assert.Empty(theirs.Records);
    }
}
