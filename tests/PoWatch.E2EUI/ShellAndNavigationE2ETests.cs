using Microsoft.Playwright;

namespace PoWatch.E2EUI;

/// <summary>
/// The app shell: chrome that must be present on every page, and navigation between them.
/// </summary>
[Collection(nameof(PlaywrightCollection))]
public sealed class ShellAndNavigationE2ETests(PlaywrightFixture fixture)
{
    [Fact]
    public async Task The_terminal_shell_renders_header_keys_command_line_and_tape()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);

        await Assertions.Expect(page.GetByTestId("term-header")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("app-navbar")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("command-line")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("ticker")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("session-status")).ToContainTextAsync("STANDBY");
        await Assertions.Expect(page.GetByTestId("settings-menu")).ToBeVisibleAsync();
        await page.AssertNoBlazorErrorAsync();
    }

    [Fact]
    public async Task Every_route_renders_with_its_section_title()
    {
        foreach (var (route, heading) in new (string route, string heading)[]
        {
            ("/", "LIVE"),
            ("/stats", "STATS"),
            ("/history", "HISTORY"),
            ("/regulars", "REGULARS"),
            ("/trophies", "TROPHIES"),
            ("/system", "SYSTEM"),
            ("/health", "SYSTEM"),
        })
        {
            if (PlaywrightFixture.BaseUrl is null) return;
            var page = await PoWatchPage.SignedInAsync(fixture.Browser);

            await page.GoToAsync(route, heading);
            await page.AssertNoBlazorErrorAsync();
        }
    }

    [Fact]
    public async Task Number_keys_the_key_bar_and_the_command_line_all_navigate()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);

        await page.Keyboard.PressAsync("2");
        await page.ExpectSectionAsync("STATS");

        await page.Keyboard.PressAsync("/");
        await page.Keyboard.TypeAsync("regulars");
        await page.Keyboard.PressAsync("Enter");
        await page.ExpectSectionAsync("REGULARS");

        await page.GetByTestId("command-line").FillAsync("nonsense");
        await page.GetByTestId("command-line").PressAsync("Enter");
        await Assertions.Expect(page.GetByTestId("command-hint")).ToContainTextAsync("Commands:");

        await page.GetByTestId("nav-live").ClickAsync();
        await page.ExpectSectionAsync("LIVE");
    }

    [Fact]
    public async Task An_unknown_route_offers_a_way_back_instead_of_dead_ending()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);

        await page.GotoAsync($"{PlaywrightFixture.BaseUrl}/no-such-page");

        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Page not found" }))
            .ToBeVisibleAsync(new() { Timeout = 30000 });
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Back to Live" }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task The_theme_toggle_switches_and_persists_across_a_reload()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);

        var before = await page.GetAttributeAsync("html", "data-theme");
        await page.OpenSettingsAsync();
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
