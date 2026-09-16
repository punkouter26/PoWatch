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

    [Fact]
    public async Task Every_route_renders_with_the_heading_the_nav_promises()
    {
        foreach (var (route, heading) in new (string route, string heading)[]
        {
            ("/", "Live Room"),
            ("/archives", "History"),
            ("/identity", "People"),
            ("/diagnostics", "System"),
            ("/health", "Health"),
        })
        {
            if (PlaywrightFixture.BaseUrl is null) return;
            var page = await PoWatchPage.SignedInAsync(fixture.Browser);

            await page.GoToAsync(route, heading);
            await page.AssertNoBlazorErrorAsync();

        }
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
    public async Task The_skip_link_is_the_first_thing_a_keyboard_user_reaches()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);

        await page.Keyboard.PressAsync("Tab");
        var focused = await page.EvaluateAsync<string>("() => document.activeElement?.textContent ?? ''");

        Assert.Contains("Skip to main content", focused, StringComparison.OrdinalIgnoreCase);
    }

}
