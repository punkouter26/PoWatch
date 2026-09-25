using Microsoft.Playwright;

namespace PoWatch.E2EUI;

/// <summary>
/// What each page actually shows: the controls people use, and the consistency rules that broke
/// silently before — one person under two names, a heading that contradicts the nav.
/// </summary>
[Collection(nameof(PlaywrightCollection))]
public sealed class PageContentE2ETests(PlaywrightFixture fixture)
{
    [Fact]
    public async Task The_live_page_shows_every_panel_and_both_ways_to_start()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);

        foreach (var panel in new[] { "live-camera", "live-metrics", "live-tracks", "live-census", "live-pipeline", "live-motion-grid", "live-light", "live-pattern", "live-wire" })
            await Assertions.Expect(page.GetByTestId(panel)).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("start-camera")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("start-demo")).ToBeVisibleAsync();
        await page.AssertNoBlazorErrorAsync();
    }

    [Fact]
    public async Task A_demo_session_fills_the_counters_within_15_seconds_and_stops_cleanly()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);

        await page.GetByTestId("start-demo").ClickAsync();
        await Assertions.Expect(page.GetByTestId("session-status")).ToContainTextAsync("OBSERVING");

        // SPEC §15 #1: nonzero live counters within 15 s, with no camera, GPU or model.
        await Assertions.Expect(page.GetByTestId("metric-visits")).Not.ToHaveTextAsync("0", new() { Timeout = 15_000 });
        await Assertions.Expect(page.GetByTestId("stat-pixel-hz")).Not.ToContainTextAsync("0.0", new() { Timeout = 15_000 });
        await Assertions.Expect(page.GetByTestId("ticker")).ToContainTextAsync("person", new() { Timeout = 15_000 });

        await page.GetByTestId("stop-session").ClickAsync();
        await Assertions.Expect(page.GetByTestId("session-status")).ToContainTextAsync("STANDBY");

        // Stopping shows the recap: the session's own numbers and its moments.
        await Assertions.Expect(page.GetByTestId("away-card")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(page.GetByTestId("away-visits")).Not.ToContainTextAsync("0", new() { Timeout = 15_000 });
        await Assertions.Expect(page.GetByTestId("away-card")).ToContainTextAsync("of the session");
        await page.GetByTestId("away-close").ClickAsync();
        await Assertions.Expect(page.GetByTestId("away-card")).Not.ToBeVisibleAsync();
        await page.AssertNoBlazorErrorAsync();
    }

    [Fact]
    public async Task Every_stats_tab_renders_real_numbers_for_a_seeded_month()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);
        var seed = await page.APIRequest.PostAsync($"{PlaywrightFixture.BaseUrl}/api/dev/seed?days=40&tz=UTC");
        Assert.True(seed.Ok, $"seed returned {seed.Status}");

        await page.GoToAsync("/stats?range=30d", "STATS");
        await Assertions.Expect(page.GetByTestId("stats-occupancy")).Not.ToContainTextAsync("0.0%", new() { Timeout = 30_000 });
        await Assertions.Expect(page.Locator("[data-test=stats-presence] .rz-chart svg").First).ToBeVisibleAsync();

        foreach (var (tab, content) in new[]
        {
            ("Space", "stats-space"),
            ("Objects", "stats-objects"),
            ("Patterns & anomalies", "stats-patterns"),
            ("Environment & captions", "stats-environment"),
            ("Pipeline", "stats-pipeline"),
        })
        {
            await page.GetByRole(AriaRole.Tab, new() { Name = tab }).ClickAsync();
            await Assertions.Expect(page.GetByTestId(content)).ToBeVisibleAsync(new() { Timeout = 30_000 });
        }

        await Assertions.Expect(page.GetByTestId("stats-all-frames")).Not.ToContainTextAsync("—");
        await page.AssertNoBlazorErrorAsync();
    }

    [Fact]
    public async Task The_people_page_shows_the_glance_grid_and_the_full_list()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);
        await page.GoToAsync("/identity", "REGULARS");

        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Everyone at a glance" }))
            .ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "All people" }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task The_people_filter_narrows_the_glance_grid()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);
        await page.GoToAsync("/identity", "REGULARS");

        // Let the grid populate first, so this asserts filtering rather than an empty database.
        await page.WaitForTimeoutAsync(2500);
        await page.GetByTestId("glance-filter").FillAsync("zzz-no-such-person");
        await page.WaitForTimeoutAsync(800);

        var cards = await page.Locator(".subject-card-grid .subject-card").CountAsync();
        Assert.Equal(0, cards);

        var text = await page.InnerTextAsync("body");
        Assert.True(
            text.Contains("No one matches", StringComparison.OrdinalIgnoreCase)
            || text.Contains("No one seen yet", StringComparison.OrdinalIgnoreCase),
            "Filtering to a name nobody has should say so, not just show an empty grid.");
    }

    [Fact]
    public async Task History_walks_back_day_by_day_through_real_sessions()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);

        // Real ingest (not the seed), so past days have the minute rollups a day view reads.
        var zone = TimeZoneInfo.Local.Id;
        var start = await page.APIRequest.PostAsync($"{PlaywrightFixture.BaseUrl}/api/sessions", new() { DataObject = new { timeZoneId = zone } });
        var sessionId = (await start.JsonAsync())!.Value.GetProperty("id").GetString();
        var todayNoon = new DateTimeOffset(DateTime.Today.AddHours(12));
        var noonToday = todayNoon > DateTimeOffset.Now ? DateTimeOffset.Now.AddMinutes(-5) : todayNoon;
        object Tick(DateTimeOffset at) => new
        {
            startUtc = at.UtcDateTime,
            pixelSamples = 40,
            motionMean = 0.2,
            motionMax = 0.4,
            luminanceMean = 0.5,
            classes = new Dictionary<string, object> { ["person"] = new { max = 1, mean = 1 } }
        };
        var batch = await page.APIRequest.PostAsync($"{PlaywrightFixture.BaseUrl}/api/sessions/{sessionId}/batches", new()
        {
            DataObject = new { batchKey = Guid.NewGuid(), ticks = new[] { Tick(noonToday), Tick(todayNoon.AddDays(-1)) } }
        });
        Assert.True(batch.Ok, $"batch returned {batch.Status}");

        await page.GoToAsync("/history", "HISTORY");
        await Assertions.Expect(page.GetByTestId("history-calendar")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("history-occupancy")).ToContainTextAsync("%", new() { Timeout = 30_000 });
        await Assertions.Expect(page.GetByTestId("history-sessions")).ToContainTextAsync("#");

        var today = await page.GetByTestId("history-day").TextContentAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Previous day" }).ClickAsync();
        await Assertions.Expect(page.GetByTestId("history-day")).Not.ToHaveTextAsync(today!);
        await Assertions.Expect(page.GetByTestId("history-occupancy")).ToContainTextAsync("%", new() { Timeout = 30_000 });

        // Arrow keys page too; a day with nothing recorded says so instead of showing zeros.
        await page.GetByTestId("history-toolbar").FocusAsync();
        await page.Keyboard.PressAsync("ArrowLeft");
        await Assertions.Expect(page.GetByTestId("history-empty")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await page.GetByRole(AriaRole.Button, new() { Name = "Go to today" }).ClickAsync();
        await Assertions.Expect(page.GetByTestId("history-day")).ToHaveTextAsync(today!);
        await page.AssertNoBlazorErrorAsync();
    }

    [Fact]
    public async Task The_system_page_reports_runtime_and_inference_state()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);
        await page.GoToAsync("/diagnostics", "SYSTEM");

        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Inference engine" }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task The_system_page_offers_a_self_test_for_every_registered_model()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);
        await page.GoToAsync("/diagnostics", "SYSTEM");
        await page.GetByTestId("diagnostics-advanced").Locator("summary").ClickAsync();

        await Assertions.Expect(page.GetByTestId("model-selftest-card")).ToBeVisibleAsync();

        // The card and the Live Room picker must offer the same models — they read one registry
        // (rule 1.5), and a row missing here would mean a model nobody can check before selecting it.
        var registered = await page.EvaluateAsync<int>(
            "async () => (await (await fetch('/model-registry.json')).json()).length");

        Assert.True(registered > 0, "model-registry.json returned no models.");
        await Assertions.Expect(page.GetByTestId("model-selftest-row")).ToHaveCountAsync(registered);
    }

    [Fact]
    public async Task The_health_page_lists_every_connection_with_a_verdict()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);
        await page.GoToAsync("/health", "HEALTH");

        await Assertions.Expect(page.GetByTestId("health-overall")).ToBeVisibleAsync(new() { Timeout = 30000 });
        // Wait for the list itself: "Checking…" also renders health-overall, so counting straight
        // after it is a race with the first fetch.
        await Assertions.Expect(page.GetByTestId("health-check-list")).ToBeVisibleAsync(new() { Timeout = 30000 });
        Assert.True(await page.GetByTestId("health-check").CountAsync() > 0);
    }

    [Fact]
    public async Task Signing_out_returns_the_operator_to_the_login_page()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);

        await page.GetByTestId("sign-out").ClickAsync();

        await Assertions.Expect(page.GetByTestId("login-shell")).ToBeVisibleAsync(new() { Timeout = 60000 });
    }

}
