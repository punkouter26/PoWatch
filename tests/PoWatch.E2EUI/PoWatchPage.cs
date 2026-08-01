using Microsoft.Playwright;

namespace PoWatch.E2EUI;

/// <summary>
/// Shared setup for UI tests: a signed-in page on a booted Blazor app.
/// <para>
/// The wait matters. A WASM cold start downloads and boots the .NET runtime, which takes far longer
/// than Playwright's 5 s default expect timeout — the original single smoke test asserted straight
/// after <c>NetworkIdle</c> and failed on any cold instance. Every test here waits for the navbar,
/// which only exists once the app has actually rendered.
/// </para>
/// </summary>
internal static class PoWatchPage
{
    /// <summary>Generous enough for a cold WASM boot on a slow host.</summary>
    private const int BootTimeoutMs = 60_000;

    public static async Task<IPage> SignedInAsync(IBrowser browser, string route = "/")
    {
        var page = await browser.NewPageAsync(new()
        {
            IgnoreHTTPSErrors = true,
            ViewportSize = new() { Width = 1600, Height = 1000 }
        });

        // BFF auth: anonymous visits redirect to /login. Sign in as the dev guest first — this sets
        // the session cookie server-side and redirects to returnUrl.
        await page.GotoAsync(
            $"{PlaywrightFixture.BaseUrl}/auth/login/fake?returnUrl={Uri.EscapeDataString(route)}");

        await Assertions.Expect(page.GetByTestId("app-navbar"))
            .ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });

        return page;
    }

    /// <summary>Navigate within the booted app and wait for the page heading to settle.</summary>
    public static async Task GoToAsync(this IPage page, string route, string expectedTitle)
    {
        await page.GotoAsync($"{PlaywrightFixture.BaseUrl}{route}");
        await Assertions.Expect(page.GetByTestId("page-hud-title"))
            .ToHaveTextAsync(expectedTitle, new() { Timeout = BootTimeoutMs });
    }
}
