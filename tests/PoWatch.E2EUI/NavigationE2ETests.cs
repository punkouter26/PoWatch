using Microsoft.Playwright;

namespace PoWatch.Tests.E2E;

/// <summary>
/// C# Playwright UI smoke tests. Point these at a running instance via the E2E_BASE_URL
/// environment variable (e.g. https://localhost:5001); the suite skips when it is unset so
/// it never fails in environments without a live server or installed browsers.
/// Prepare browsers once with: pwsh bin/Debug/net10.0/playwright.ps1 install
/// </summary>
public sealed class NavigationE2ETests : IAsyncLifetime
{
    private static string? BaseUrl => Environment.GetEnvironmentVariable("E2E_BASE_URL");

    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public async Task InitializeAsync()
    {
        if (BaseUrl is null) return;
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
    }

    public async Task DisposeAsync()
    {
        if (_browser is not null) await _browser.DisposeAsync();
        _playwright?.Dispose();
    }

    [Fact]
    public async Task Home_page_loads_and_shows_the_navbar()
    {
        if (BaseUrl is null) return; // no live server configured — nothing to exercise

        var page = await _browser!.NewPageAsync(new() { IgnoreHTTPSErrors = true });
        await page.GotoAsync(BaseUrl!, new() { WaitUntil = WaitUntilState.NetworkIdle });

        await Assertions.Expect(page.Locator("nav.app-navbar")).ToBeVisibleAsync();
    }
}
