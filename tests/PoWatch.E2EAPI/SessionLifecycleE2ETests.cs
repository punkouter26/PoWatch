using System.Net;
using System.Net.Http.Json;
using PoWatch.Shared.Models;

namespace PoWatch.E2EAPI;

/// <summary>Turn it on, walk away, come back: the session lifecycle as the client drives it.</summary>
public sealed class SessionLifecycleE2ETests(ApiE2EFactory factory) : IClassFixture<ApiE2EFactory>
{
    private HttpClient ClientFor(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Fake-User", user);
        return client;
    }

    [Fact]
    public async Task A_session_starts_lists_and_stops_once()
    {
        using var client = ClientFor($"sessions-{Guid.NewGuid():N}");
        var id = Guid.NewGuid();

        var start = await client.PostAsJsonAsync("/api/sessions", new StartSessionRequestDto { SessionId = id, TimeZoneId = "America/New_York" });
        Assert.Equal(HttpStatusCode.Created, start.StatusCode);
        var started = await start.Content.ReadFromJsonAsync<SessionDto>();
        Assert.Equal(id, started!.Id);
        Assert.True(started.IsRunning);
        Assert.Equal("America/New_York", started.TimeZoneId);

        // Starting the same session again (a retried request) returns it rather than a second one.
        var retry = await client.PostAsJsonAsync("/api/sessions", new StartSessionRequestDto { SessionId = id, TimeZoneId = "America/New_York" });
        Assert.Equal(started.StartedUtc, (await retry.Content.ReadFromJsonAsync<SessionDto>())!.StartedUtc);

        var listed = await client.GetFromJsonAsync<List<SessionDto>>("/api/sessions");
        Assert.Equal([id], listed!.Select(s => s.Id));

        var stop = await client.PostAsync($"/api/sessions/{id}/stop", null);
        Assert.Equal(HttpStatusCode.OK, stop.StatusCode);
        var stopped = await stop.Content.ReadFromJsonAsync<SessionDto>();
        Assert.False(stopped!.IsRunning);
        Assert.NotNull(stopped.EndedUtc);

        // Stopping twice keeps the first end time.
        var again = await (await client.PostAsync($"/api/sessions/{id}/stop", null)).Content.ReadFromJsonAsync<SessionDto>();
        Assert.Equal(stopped.EndedUtc, again!.EndedUtc);

        var fetched = await client.GetFromJsonAsync<SessionDto>($"/api/sessions/{id}");
        Assert.Equal(stopped.EndedUtc, fetched!.EndedUtc);
    }

    [Fact]
    public async Task Sessions_are_private_and_bad_input_is_rejected()
    {
        using var owner = ClientFor($"owner-{Guid.NewGuid():N}");
        using var stranger = ClientFor($"stranger-{Guid.NewGuid():N}");

        var started = await (await owner.PostAsJsonAsync("/api/sessions", new StartSessionRequestDto { TimeZoneId = "UTC" }))
            .Content.ReadFromJsonAsync<SessionDto>();

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/sessions/{started!.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync($"/api/sessions/{started.Id}/stop", null)).StatusCode);
        Assert.Empty((await stranger.GetFromJsonAsync<List<SessionDto>>("/api/sessions"))!);

        var badZone = await owner.PostAsJsonAsync("/api/sessions", new StartSessionRequestDto { TimeZoneId = "Mars/Olympus_Mons" });
        Assert.Equal(HttpStatusCode.BadRequest, badZone.StatusCode);
    }
}
