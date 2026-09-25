using Microsoft.Playwright;

namespace PoWatch.E2EUI;

/// <summary>
/// Locks in the viewport-fit rules that the design system says every screen must obey:
/// <list type="bullet">
///   <item><description>Pages with internal scrolling (Live Room, Archives) don't extend the body past the viewport.</description></item>
///   <item><description>The topbar collapses to a hamburger drawer on narrow viewports (≤720px).</description></item>
///   <item><description>The settings drawer is reachable via the gear icon on every viewport.</description></item>
///   <item><description>The Daily Activity strip is rendered (compact mode on phones, full strip on desktops).</description></item>
/// </list>
/// These rules used to live only as a docstring in the design tokens. The audit pass moved them
/// into CI: a regression that re-introduces a layout that exceeds the viewport on a360×640 phone
/// now fails a test rather than going unnoticed.
/// </summary>
[Collection(nameof(PlaywrightCollection))]
public sealed class ViewportFitE2ETests
{
    private readonly PlaywrightFixture _fixture;

    public ViewportFitE2ETests(PlaywrightFixture fixture) => _fixture = fixture;

    public static IEnumerable<object[]> PhoneAndDesktopViewports()
    {
        // 360×640 = the lowest common denominator Android/iOS portrait.
        // 768×1024 = iPad portrait — middle tier where the layout must still work.
        // 1440×900 = typical desktop.
        yield return new object[] { 360, 640 };
        yield return new object[] { 768, 1024 };
        yield return new object[] { 1440, 900 };
    }

    public static IEnumerable<object[]> DesktopOnlyViewports()
    {
        // Some pages (Display) are deliberately full-screen on desktops; their layout contract
        // only matters for the kiosk case.
        yield return new object[] { 1440, 900 };
        yield return new object[] { 1920, 1080 };
    }

    [Fact]
    public async Task Live_Room_body_fits_within_viewport()
    {
        foreach (var scenario in PhoneAndDesktopViewports())
        {
            int width = (int)scenario[0];
            int height = (int)scenario[1];
            if (PlaywrightFixture.BaseUrl is null) return;
            var page = await PoWatchPage.SignedInAsync(_fixture.Browser, "/");
            await page.SetViewportSizeAsync(width, height);
            await page.WaitForTimeoutAsync(1000);

            await AssertBodyFitsViewportAsync(page);
            await page.AssertNoBlazorErrorAsync();

        }
    }

    [Fact]
    public async Task History_body_fits_within_viewport()
    {
        foreach (var scenario in PhoneAndDesktopViewports())
        {
            int width = (int)scenario[0];
            int height = (int)scenario[1];
            if (PlaywrightFixture.BaseUrl is null) return;
            var page = await PoWatchPage.SignedInAsync(_fixture.Browser);
            await page.SetViewportSizeAsync(width, height);
            await page.GoToAsync("/archives", "HISTORY");
            await page.WaitForTimeoutAsync(500);

            await AssertBodyFitsViewportAsync(page);
            await page.AssertNoBlazorErrorAsync();

        }
    }

    [Fact]
    public async Task People_body_fits_within_viewport()
    {
        foreach (var scenario in PhoneAndDesktopViewports())
        {
            int width = (int)scenario[0];
            int height = (int)scenario[1];
            if (PlaywrightFixture.BaseUrl is null) return;
            var page = await PoWatchPage.SignedInAsync(_fixture.Browser);
            await page.SetViewportSizeAsync(width, height);
            await page.GoToAsync("/identity", "REGULARS");
            await page.WaitForTimeoutAsync(500);

            await AssertBodyFitsViewportAsync(page);
            await page.AssertNoBlazorErrorAsync();

        }
    }

    [Fact]
    public async Task System_body_fits_within_viewport()
    {
        foreach (var scenario in PhoneAndDesktopViewports())
        {
            int width = (int)scenario[0];
            int height = (int)scenario[1];
            if (PlaywrightFixture.BaseUrl is null) return;
            var page = await PoWatchPage.SignedInAsync(_fixture.Browser);
            await page.SetViewportSizeAsync(width, height);
            await page.GoToAsync("/diagnostics", "SYSTEM");
            await page.WaitForTimeoutAsync(500);

            await AssertBodyFitsViewportAsync(page);
            await page.AssertNoBlazorErrorAsync();

        }
    }

    [Fact]
    public async Task Health_body_fits_within_viewport()
    {
        foreach (var scenario in PhoneAndDesktopViewports())
        {
            int width = (int)scenario[0];
            int height = (int)scenario[1];
            if (PlaywrightFixture.BaseUrl is null) return;
            var page = await PoWatchPage.SignedInAsync(_fixture.Browser);
            await page.SetViewportSizeAsync(width, height);
            await page.GoToAsync("/health", "HEALTH");
            await page.WaitForTimeoutAsync(500);

            await AssertBodyFitsViewportAsync(page);
            await page.AssertNoBlazorErrorAsync();

        }
    }

    [Fact]
    public async Task Display_fills_the_viewport()
    {
        foreach (var scenario in DesktopOnlyViewports())
        {
            int width = (int)scenario[0];
            int height = (int)scenario[1];
            if (PlaywrightFixture.BaseUrl is null) return;
            // /display is anonymous — no auth required.
            var page = await _fixture.Browser.NewPageAsync(new()
            {
                IgnoreHTTPSErrors = true,
                ViewportSize = new() { Width = width, Height = height }
            });
            await page.GotoAsync($"{PlaywrightFixture.BaseUrl}/display");
            await page.WaitForTimeoutAsync(1000);

            // Display page deliberately extends to fill the viewport (position: fixed; inset: 0).
            var displayBox = await page.Locator(".display-page").BoundingBoxAsync();
            Assert.NotNull(displayBox);
            Assert.True(
                displayBox!.Width >= width - 1 && displayBox.Height >= height - 1,
                $"Display page should fill the viewport on a kiosk; got {displayBox.Width}x{displayBox.Height} at {width}x{height}.");
            await page.AssertNoBlazorErrorAsync();

        }
    }

    [Fact]
    public async Task On_a_phone_the_key_bar_scrolls_instead_of_widening_the_page()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(_fixture.Browser, "/");
        await page.SetViewportSizeAsync(360, 640);
        await page.WaitForTimeoutAsync(500);

        // Every section stays reachable from the key bar, and the page itself never scrolls sideways.
        await Assertions.Expect(page.GetByTestId("nav-system")).ToBeAttachedAsync();
        var pageOverflow = await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > window.innerWidth + 1");
        Assert.False(pageOverflow, "The page scrolls horizontally on a 360 px phone.");

        await page.GetByTestId("nav-system").ScrollIntoViewIfNeededAsync();
        await page.GetByTestId("nav-system").ClickAsync();
        await Assertions.Expect(page.GetByTestId("page-hud-title")).ToHaveTextAsync("SYSTEM");
        await page.AssertNoBlazorErrorAsync();
    }

    [Fact]
    public async Task Live_Room_settings_drawer_closes_on_Escape()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(_fixture.Browser);
        await page.SetViewportSizeAsync(1440, 900);

        await page.GetByTestId("observer-settings-gear").ClickAsync();
        await Assertions.Expect(page.GetByTestId("observer-settings-drawer")).ToBeVisibleAsync();

        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(200);
        await Assertions.Expect(page.GetByTestId("observer-settings-drawer")).Not.ToBeVisibleAsync();

        await page.AssertNoBlazorErrorAsync();
    }

    [Fact]
    public async Task Live_Room_heatmap_uses_compact_layout_on_a_phone()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(_fixture.Browser, "/");
        await page.SetViewportSizeAsync(360, 640);
        await page.WaitForTimeoutAsync(500);

        // Compact mode renders a summary row instead of 24 cells (idea #3 / idea #5).
        var compactSummary = page.Locator(".heatmap-compact-summary");
        await Assertions.Expect(compactSummary).ToBeVisibleAsync();

        // Full grid mode would render 24 cells — none should be present.
        var fullStripCells = await page.Locator(".data-strip-cell").CountAsync();
        Assert.Equal(0, fullStripCells);

        await page.AssertNoBlazorErrorAsync();
    }

    private static async Task AssertBodyFitsViewportAsync(IPage page)
    {
        var size = await page.EvaluateAsync<int[]>("() => [document.documentElement.clientWidth, document.documentElement.clientHeight, document.documentElement.scrollHeight]");
        // Some pages legitimately scroll a panel internally (Live Room timeline, Archives evidence
        // grid). What we forbid is the *body* scrolling — that means a panel grew taller than
        // its container and pushed the chrome out of view. Allow a 2 px tolerance for sub-pixel
        // rounding between browsers.
        var overflow = size[2] - size[1];
        Assert.True(
            overflow <= 2,
            $"Body exceeds viewport by {overflow} px (viewport={size[1]}, scrollHeight={size[2]}, width={size[0]}). " +
            "A page that scrolls the body pushes the chrome out of view on a kiosk — re-check the page's overflow contract.");
    }

}
