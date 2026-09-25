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

        foreach (var panel in new[] { "live-camera", "live-metrics", "live-tracks", "live-motion-grid", "live-light" })
            await Assertions.Expect(page.GetByTestId(panel)).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("start-camera")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("start-demo")).ToBeVisibleAsync();
        await page.AssertNoBlazorErrorAsync();
    }

    [Fact]
    public async Task A_demo_session_fills_the_counters_unlocks_a_trophy_and_stops_cleanly()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        // A fresh user, so First Light is still locked whatever other tests ran first.
        var page = await PoWatchPage.SignedInAsync(fixture.Browser, user: $"demo-{Guid.NewGuid():N}");
        // Unlock toasts ride the stats hub; wait for it so the session-start unlock is not missed.
        await Assertions.Expect(page.GetByTestId("trophy-toasts")).ToHaveAttributeAsync("data-hub", "up", new() { Timeout = 15_000 });

        await page.GetByTestId("start-demo").ClickAsync();
        await Assertions.Expect(page.GetByTestId("session-status")).ToContainTextAsync("OBSERVING");
        // A first session unlocks First Light, announced by a toast pushed over the stats hub.
        await Assertions.Expect(page.GetByTestId("trophy-toasts")).ToContainTextAsync("First Light", new() { Timeout = 10_000 });

        // nonzero live counters within 15 s, with no camera, GPU or model.
        await Assertions.Expect(page.GetByTestId("metric-visits")).Not.ToHaveTextAsync("0", new() { Timeout = 15_000 });
        await Assertions.Expect(page.GetByTestId("stat-pixel-hz")).Not.ToContainTextAsync("0.0", new() { Timeout = 15_000 });

        await page.GetByTestId("header-stop").ClickAsync();
        await Assertions.Expect(page.GetByTestId("session-status")).ToContainTextAsync("STANDBY");

        // Stopping shows the recap: the session's own numbers and its moments.
        await Assertions.Expect(page.GetByTestId("away-card")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(page.GetByTestId("away-visits")).Not.ToContainTextAsync("0", new() { Timeout = 15_000 });
        await Assertions.Expect(page.GetByTestId("away-card")).ToContainTextAsync("of the session");
        await Assertions.Expect(page.GetByTestId("away-pdf")).ToHaveAttributeAsync("href", new System.Text.RegularExpressions.Regex(@"^api/recaps/session/.+\.pdf$"));
        await page.GetByTestId("away-close").ClickAsync();
        await Assertions.Expect(page.GetByTestId("away-card")).Not.ToBeVisibleAsync();

        await page.GoToAsync("/trophies", "TROPHIES");
        await Assertions.Expect(page.Locator("[data-test=trophy][data-unlocked=true]").Filter(new() { HasText = "First Light" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("record-row").Filter(new() { HasText = "Longest session" })).ToBeVisibleAsync();
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
            ("Objects", "stats-objects"),
            ("Patterns", "stats-patterns"),
            ("Environment", "stats-environment"),
        })
        {
            await page.GetByRole(AriaRole.Tab, new() { Name = tab }).ClickAsync();
            await Assertions.Expect(page.GetByTestId(content)).ToBeVisibleAsync(new() { Timeout = 30_000 });
        }

        // Presence and space share a tab; pipeline counters moved to System.
        await page.GetByRole(AriaRole.Tab, new() { Name = "Presence & space" }).ClickAsync();
        await Assertions.Expect(page.GetByTestId("stats-space")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await page.GoToAsync("/system", "SYSTEM");
        await Assertions.Expect(page.GetByTestId("stats-all-frames")).Not.ToContainTextAsync("—", new() { Timeout = 30_000 });
        await page.AssertNoBlazorErrorAsync();
    }

    [Fact]
    public async Task New_people_can_be_named_from_a_passive_prompt_or_left_unnamed()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        // A fresh user: storage persists (Azurite), so a reused guest already knows the walker and the cat.
        var page = await PoWatchPage.SignedInAsync(fixture.Browser, user: $"names-{Guid.NewGuid():N}");

        await page.GetByTestId("start-demo").ClickAsync();

        // The demo walker and the cat each get an offer to be named; nothing blocks the page meanwhile.
        var person = page.GetByTestId("name-prompt").Filter(new() { HasText = "New person spotted" });
        var cat = page.GetByTestId("name-prompt").Filter(new() { HasText = "New cat spotted" });
        await Assertions.Expect(person).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Assertions.Expect(cat).ToBeVisibleAsync(new() { Timeout = 20_000 });

        await person.GetByTestId("name-input").FillAsync("Bob");
        await person.GetByTestId("name-save").ClickAsync();
        await Assertions.Expect(person).Not.ToBeVisibleAsync();
        await cat.GetByTestId("name-skip").ClickAsync();
        await Assertions.Expect(cat).Not.ToBeVisibleAsync();

        // Bob is recognised by name on the live overlay, and listed on Regulars; the cat stays unnamed.
        await Assertions.Expect(page.GetByTestId("live-tracks")).ToContainTextAsync("Bob", new() { Timeout = 25_000 });
        await page.GetByTestId("header-stop").ClickAsync();
        await page.GetByTestId("nav-regulars").ClickAsync();
        await Assertions.Expect(page.GetByTestId("regulars-table")).ToBeVisibleAsync();
        ILocator NameBox(string who) => page.Locator(".regular-name").Filter(new() { HasText = $"Name for {who}" }).Locator("input");
        await Assertions.Expect(NameBox("Bob")).ToHaveValueAsync("Bob");
        await Assertions.Expect(NameBox("Cat 1")).ToHaveValueAsync("");
        await page.AssertNoBlazorErrorAsync();
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
        await Assertions.Expect(page.GetByTestId("history-timelapse")).ToContainTextAsync("FRAMES · THIS DEVICE");

        // The day's recap reads as a sentence and downloads as a real PDF.
        await Assertions.Expect(page.GetByTestId("history-recap")).ToContainTextAsync("WRITTEN BY TEMPLATE");
        var pdfHref = await page.GetByTestId("history-recap-pdf").GetAttributeAsync("href");
        var pdf = await page.APIRequest.GetAsync($"{PlaywrightFixture.BaseUrl}/{pdfHref}");
        Assert.True(pdf.Ok || pdf.Status == 503, $"recap PDF returned {pdf.Status}");
        if (pdf.Ok) Assert.Equal("%PDF"u8.ToArray(), (await pdf.BodyAsync()).Take(4).ToArray());

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
    public async Task The_system_page_lists_every_connection_with_a_verdict()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);
        // The old /health address still works; it is the System page now.
        await page.GoToAsync("/health", "SYSTEM");

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

        await page.OpenSettingsAsync();
        await page.GetByTestId("sign-out").ClickAsync();

        await Assertions.Expect(page.GetByTestId("login-shell")).ToBeVisibleAsync(new() { Timeout = 60000 });
    }

}
