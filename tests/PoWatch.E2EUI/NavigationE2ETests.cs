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

        // SignedInAsync waits for the navbar with a cold-WASM-boot timeout. This test used to
        // assert straight after NetworkIdle with Playwright's 5 s default, which is far less than
        // a first-load .NET runtime download — so it failed on any instance that was not warm.
        var page = await PoWatchPage.SignedInAsync(_fixture.Browser);

        // Stable selectors — see data-test attributes on MainLayout / NavMenu.
        await Assertions.Expect(page.GetByTestId("page-hud")).ToBeVisibleAsync();

        await page.AssertNoBlazorErrorAsync();
    }
}
