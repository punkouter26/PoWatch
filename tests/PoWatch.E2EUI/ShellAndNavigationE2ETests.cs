using Microsoft.Playwright;

namespace PoWatch.E2EUI;

/// <summary>
/// The app shell: chrome that must be present on every page, and navigation between them.
/// </summary>
[Collection(nameof(PlaywrightCollection))]
public sealed class ShellAndNavigationE2ETests(PlaywrightFixture fixture)
{
    [Fact]
    public async Task The_shell_renders_with_branding_navigation_and_session_controls()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);

        await Assertions.Expect(page.GetByTestId("app-navbar")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("navbar-brand")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("page-hud")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("navbar-sign-out")).ToBeVisibleAsync();
        await page.AssertNoBlazorErrorAsync();
    }

    [Theory]
    [InlineData("/", "Live Room")]
    [InlineData("/archives", "History")]
    [InlineData("/identity", "People")]
    [InlineData("/diagnostics", "System")]
    [InlineData("/health", "Health")]
    public async Task Every_route_renders_with_the_heading_the_nav_promises(string route, string heading)
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);

        await page.GoToAsync(route, heading);
        await page.AssertNoBlazorErrorAsync();
    }

    [Fact]
    public async Task The_three_primary_nav_links_move_between_pages()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);

        await page.GetByTestId("nav-link-archives").ClickAsync();
        await Assertions.Expect(page.GetByTestId("page-hud-title")).ToHaveTextAsync("History");

        await page.GetByTestId("nav-link-identity").ClickAsync();
        await Assertions.Expect(page.GetByTestId("page-hud-title")).ToHaveTextAsync("People");

        await page.GetByTestId("nav-link-observer-hub").ClickAsync();
        await Assertions.Expect(page.GetByTestId("page-hud-title")).ToHaveTextAsync("Live Room");
    }

    [Fact]
    public async Task The_gear_and_heart_rail_reach_System_and_Health()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);

        await page.GetByTestId("nav-link-diagnostics").ClickAsync();
        await Assertions.Expect(page.GetByTestId("page-hud-title")).ToHaveTextAsync("System");

        await page.GetByTestId("nav-link-health").ClickAsync();
        await Assertions.Expect(page.GetByTestId("page-hud-title")).ToHaveTextAsync("Health");
    }

    [Fact]
    public async Task The_page_heading_changes_with_the_route_not_after_it()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);

        await page.GetByTestId("nav-link-archives").ClickAsync();

        // The heading used to be updated inside a fire-and-forget 350 ms continuation, so it still
        // announced the previous page during the transition. A short timeout catches a regression.
        await Assertions.Expect(page.GetByTestId("page-hud-title"))
            .ToHaveTextAsync("History", new() { Timeout = 2000 });
    }

    [Fact]
    public async Task An_unknown_route_offers_a_way_back_instead_of_dead_ending()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);

        await page.GotoAsync($"{PlaywrightFixture.BaseUrl}/no-such-page");

        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Page not found" }))
            .ToBeVisibleAsync(new() { Timeout = 30000 });
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Live Room — start watching the room" }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task The_404_page_speaks_the_same_language_as_the_rest_of_the_app()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);

        await page.GotoAsync($"{PlaywrightFixture.BaseUrl}/no-such-page");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Page not found" }))
            .ToBeVisibleAsync(new() { Timeout = 30000 });

        // It used to say "Sector Not Found" / "command center map" / "four sectors" — vocabulary
        // that appears nowhere else in this app.
        var body = await page.InnerTextAsync("body");
        Assert.DoesNotContain("Sector", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("command center", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_theme_toggle_switches_and_persists_across_a_reload()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);

        var before = await page.GetAttributeAsync("html", "data-theme");
        await page.GetByTestId("theme-toggle").ClickAsync();
        await page.WaitForTimeoutAsync(400);
        var after = await page.GetAttributeAsync("html", "data-theme");

        Assert.NotEqual(before, after);

        await page.ReloadAsync();
        await Assertions.Expect(page.GetByTestId("app-navbar")).ToBeVisibleAsync(new() { Timeout = 60000 });
        Assert.Equal(after, await page.GetAttributeAsync("html", "data-theme"));
    }

    [Fact]
    public async Task Ctrl_R_still_reloads_the_page()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);

        // The global keydown handler used to preventDefault() Ctrl+R and dispatch an event nothing
        // listened to, so the browser's reload — the one recovery gesture on a kiosk — was dead.
        var defaultPrevented = await page.EvaluateAsync<bool>(@"() => {
            const e = new KeyboardEvent('keydown', { key: 'r', ctrlKey: true, cancelable: true, bubbles: true });
            document.dispatchEvent(e);
            return e.defaultPrevented;
        }");

        Assert.False(defaultPrevented);
    }

    [Fact]
    public async Task Arrow_keys_are_not_swallowed_outside_the_history_page()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);

        var defaultPrevented = await page.EvaluateAsync<bool>(@"() => {
            const e = new KeyboardEvent('keydown', { key: 'ArrowLeft', cancelable: true, bubbles: true });
            document.dispatchEvent(e);
            return e.defaultPrevented;
        }");

        Assert.False(defaultPrevented);
    }

    [Fact]
    public async Task The_skip_link_is_the_first_thing_a_keyboard_user_reaches()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);

        await page.Keyboard.PressAsync("Tab");
        var focused = await page.EvaluateAsync<string>("() => document.activeElement?.textContent ?? ''");

        Assert.Contains("Skip to main content", focused, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_dead_webcam_shell_bridge_is_no_longer_shipped()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);

        var present = await page.EvaluateAsync<bool>("() => typeof window.powatchWebcamShell !== 'undefined'");

        Assert.False(present);
    }

    [Fact]
    public async Task No_page_reports_a_console_error()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);

        var errors = new List<string>();
        page.Console += (_, m) => { if (m.Type == "error") errors.Add(m.Text); };
        page.PageError += (_, e) => errors.Add(e);

        foreach (var (route, heading) in new[]
                 {
                     ("/", "Live Room"), ("/archives", "History"),
                     ("/identity", "People"), ("/diagnostics", "System"), ("/health", "Health")
                 })
        {
            await page.GoToAsync(route, heading);
        }

        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }
}
