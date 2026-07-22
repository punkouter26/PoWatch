using Microsoft.Playwright;

namespace PoWatch.E2EUI;

/// <summary>
/// C# Playwright UI smoke tests. Point these at a running instance via the E2E_BASE_URL
/// environment variable (e.g. https://localhost:5001); the suite skips when it is unset so
/// it never fails in environments without a live server or installed browsers.
/// Prepare browsers once with: pwsh bin/Debug/net10.0/playwright.ps1 install
/// </summary>
[Collection(nameof(PlaywrightCollection))]
public sealed class NavigationE2ETests
{
    private readonly PlaywrightFixture _fixture;

    public NavigationE2ETests(PlaywrightFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Home_page_loads_and_shows_the_navbar()
    {
        if (PlaywrightFixture.BaseUrl is null) return; // no live server configured — nothing to exercise

        var page = await _fixture.Browser.NewPageAsync(new() { IgnoreHTTPSErrors = true });

        // BFF auth: anonymous visits redirect to /login, which has no navbar. Sign in as the
        // dev guest first (sets the session cookie, then redirects to returnUrl).
        await page.GotoAsync($"{PlaywrightFixture.BaseUrl}/auth/login/fake?returnUrl=%2F",
            new() { WaitUntil = WaitUntilState.NetworkIdle });

        // Stable selectors — see data-test attributes on MainLayout / NavMenu.
        await Assertions.Expect(page.GetByTestId("app-navbar")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("page-hud")).ToBeVisibleAsync();

        await page.AssertNoBlazorErrorAsync();
    }
}
