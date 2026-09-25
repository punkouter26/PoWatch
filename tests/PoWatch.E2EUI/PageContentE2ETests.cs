using Microsoft.Playwright;
using System.Text.Json.Nodes;

namespace PoWatch.E2EUI;

/// <summary>
/// What each page actually shows: the controls a caregiver uses, and the consistency rules that
/// broke silently before — one person under two names, a heading that contradicts the nav,
/// a destructive action given top billing.
/// </summary>
[Collection(nameof(PlaywrightCollection))]
public sealed class PageContentE2ETests(PlaywrightFixture fixture)
{
    [Fact]
    public async Task The_live_room_leads_with_room_state_and_one_obvious_action()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);

        await Assertions.Expect(page.GetByTestId("room-status-hero")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("hero-start")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("hero-handoff")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Observer_settings_open_in_a_drawer_and_close_again()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);

        await page.GetByTestId("observer-settings-gear").ClickAsync();
        await Assertions.Expect(page.GetByTestId("observer-settings-drawer")).ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "Close settings" }).ClickAsync();
        await Assertions.Expect(page.GetByTestId("observer-settings-drawer")).Not.ToBeVisibleAsync();
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
    public async Task The_history_page_offers_day_navigation()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);

        // A rendering failure must stay on its page instead of poisoning later navigation.
        var archiveRoute = $"{PlaywrightFixture.BaseUrl}/api/archives/*";
        await page.RouteAsync(archiveRoute, async route =>
        {
            var response = await route.FetchAsync();
            var chapter = JsonNode.Parse(await response.TextAsync())!.AsObject();
            chapter["timeline"] = null;
            chapter["highlights"] = new JsonArray();
            await route.FulfillAsync(new() { Response = response, Body = chapter.ToJsonString() });
        });
        await page.GetByTestId("nav-history").ClickAsync();
        await Assertions.Expect(page.Locator(".po-error-panel")).ToBeVisibleAsync(new() { Timeout = 30000 });
        await page.UnrouteAsync(archiveRoute);

        await page.GetByTestId("nav-live").ClickAsync();
        await Assertions.Expect(page.GetByTestId("hero-start")).ToBeVisibleAsync();
        await page.GetByTestId("nav-history").ClickAsync();

        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Previous day" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Go to today" })).ToBeVisibleAsync();
        await page.AssertNoBlazorErrorAsync();

        // A network failure must offer recovery before opening the handoff dialog.
        await page.RouteAsync(archiveRoute, route => route.AbortAsync("failed"));
        await page.GotoAsync($"{PlaywrightFixture.BaseUrl}/archives?handoff=1&shift=Afternoon");
        await Assertions.Expect(page.GetByTestId("history-load-error")).ToBeVisibleAsync(new() { Timeout = 30000 });
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Generate shift brief" })).Not.ToBeVisibleAsync();
        await page.UnrouteAsync(archiveRoute);
        await page.GetByRole(AriaRole.Button, new() { Name = "Try again" }).ClickAsync();
        await Assertions.Expect(page.GetByTestId("history-load-error")).Not.ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Generate shift brief" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Handoff brief", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 30000 });
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
