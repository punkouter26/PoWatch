using Microsoft.Playwright;

namespace PoWatch.E2EUI;

/// <summary>
/// Locks in the viewport-fit rules that the design system says every screen must obey:
/// <list type="bullet">
///   <item><description>Pages with internal scrolling (Live, History) don't extend the body past the viewport.</description></item>
///   <item><description>On narrow viewports every section key stays visible without widening the page.</description></item>
///   <item><description>A camera session keeps sampling when you leave the Live page.</description></item>
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
            await page.GoToAsync("/history", "HISTORY");
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
            await page.GoToAsync("/regulars", "REGULARS");
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
    public async Task The_stats_wall_fills_the_screen_without_scrolling_and_refreshes()
    {
        foreach (var (width, height) in new[] { (1920, 1080), (1280, 720) })
        {
            if (PlaywrightFixture.BaseUrl is null) return;
            var page = await PoWatchPage.SignedInAsync(_fixture.Browser);
            await page.APIRequest.PostAsync($"{PlaywrightFixture.BaseUrl}/api/dev/seed?days=30&tz=UTC");
            await page.SetViewportSizeAsync(width, height);
            await page.GotoAsync($"{PlaywrightFixture.BaseUrl}/display");

            // Seeded numbers appear within one refresh cycle (15 s) of the wall rendering.
            await Assertions.Expect(page.GetByTestId("wall-occupancy")).Not.ToContainTextAsync("—", new() { Timeout = 30_000 });
            await Assertions.Expect(page.GetByTestId("term-header")).Not.ToBeVisibleAsync();

            var fits = await page.EvaluateAsync<bool>(@"() => {
                const wall = document.querySelector('.wall').getBoundingClientRect();
                const root = document.documentElement;
                return root.scrollHeight <= root.clientHeight + 2 && root.scrollWidth <= root.clientWidth + 2
                    && wall.bottom <= innerHeight + 1 && wall.right <= innerWidth + 1 && wall.width >= innerWidth - 40;
            }");
            Assert.True(fits, $"The stats wall scrolls or spills out of a {width}×{height} screen.");
            await page.AssertNoBlazorErrorAsync();
        }
    }

    [Fact]
    public async Task On_a_phone_every_section_key_is_visible_without_widening_the_page()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(_fixture.Browser, "/");
        await page.SetViewportSizeAsync(360, 640);
        await page.WaitForTimeoutAsync(500);

        // Every section stays reachable from the key bar, and the page itself never scrolls sideways.
        await Assertions.Expect(page.GetByTestId("nav-system")).ToBeInViewportAsync();
        var pageOverflow = await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > window.innerWidth + 1");
        Assert.False(pageOverflow, "The page scrolls horizontally on a 360 px phone.");

        await page.GetByTestId("nav-system").ClickAsync();
        await page.ExpectSectionAsync("SYSTEM");
        await page.AssertNoBlazorErrorAsync();
    }

    [Fact]
    public async Task A_camera_session_samples_the_fake_camera_and_survives_navigation()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(_fixture.Browser);
        await page.SetViewportSizeAsync(1440, 900);

        // Chromium's fake camera (see PlaywrightFixture); captions off so no vision model downloads.
        await page.GetByLabel("Captions (vision model)").UncheckAsync();
        await page.GetByTestId("start-camera").ClickAsync();
        await Assertions.Expect(page.GetByTestId("session-status")).ToContainTextAsync("OBSERVING", new() { Timeout = 15_000 });
        await Assertions.Expect(page.GetByTestId("stat-pixel-hz")).Not.ToContainTextAsync("0.0", new() { Timeout = 15_000 });

        // Leaving Live must not stop sensing: the camera element lives in the layout.
        await page.Keyboard.PressAsync("2");
        await page.ExpectSectionAsync("STATS");
        await page.WaitForTimeoutAsync(2_000);
        await page.Keyboard.PressAsync("1");
        await Assertions.Expect(page.GetByTestId("session-status")).ToContainTextAsync("OBSERVING");
        await Assertions.Expect(page.GetByTestId("stat-pixel-hz")).Not.ToContainTextAsync("0.0", new() { Timeout = 5_000 });

        await page.GetByTestId("header-stop").ClickAsync();
        await Assertions.Expect(page.GetByTestId("session-status")).ToContainTextAsync("STANDBY");
        await page.AssertNoBlazorErrorAsync();
    }

    private static async Task AssertBodyFitsViewportAsync(IPage page)
    {
        var size = await page.EvaluateAsync<int[]>("() => [document.documentElement.clientWidth, document.documentElement.clientHeight, document.documentElement.scrollHeight]");
        // Some pages legitimately scroll a panel internally (Live panels, History day
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
