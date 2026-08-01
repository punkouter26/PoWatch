using Microsoft.Playwright;

namespace PoWatch.E2EUI;

/// <summary>
/// Shared xUnit collection fixture: spins up a single Chromium instance for all UI tests
/// so each test doesn't pay the ~250 ms browser-launch cost. Tests get their own isolated
/// <see cref="IBrowserContext"/> + <see cref="IPage"/> so cookies/storage don't bleed.
/// </summary>
public sealed class PlaywrightFixture : IAsyncLifetime
{
    /// <summary>
    /// Set to the deployed base URL (e.g. https://localhost:5001). When null the entire
    /// collection is skipped so headless builds without a live server still succeed.
    /// </summary>
    public static string? BaseUrl => Environment.GetEnvironmentVariable("E2E_BASE_URL");

    // Local dev cert is untrusted by Chromium — ignore for E2E runs.
    private static readonly string[] LaunchArgs = ["--ignore-certificate-errors"];

    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public IBrowser Browser => _browser
        ?? throw new InvalidOperationException("Browser not initialized — did the fixture skip?");

    public async Task InitializeAsync()
    {
        if (BaseUrl is null) return;
        _playwright = await Playwright.CreateAsync();
        // The app's stable selectors use data-test (see MainLayout/NavMenu), not Playwright's
        // default data-testid — align GetByTestId with the markup.
        _playwright.Selectors.SetTestIdAttribute("data-test");
        _browser = await _playwright.Chromium.LaunchAsync(new()
        {
            // E2E_HEADED=1 opens a visible browser window (demo/debug); default stays headless for CI.
            Headless = Environment.GetEnvironmentVariable("E2E_HEADED") != "1",
            Args = LaunchArgs
        });
    }

    public async Task DisposeAsync()
    {
        if (_browser is not null) await _browser.DisposeAsync();
        _playwright?.Dispose();
    }
}

[CollectionDefinition(nameof(PlaywrightCollection))]
public sealed class PlaywrightCollection : ICollectionFixture<PlaywrightFixture> { }

/// <summary>
/// Helpers layered on top of the playwright-blazor skill's standard patterns.
/// </summary>
public static class PlaywrightPageExtensions
{
    /// <summary>
    /// Asserts Blazor's <c>#blazor-error-ui</c> overlay is hidden. Blazor populates this
    /// when an unhandled exception escapes a component — a silent failure mode that
    /// would otherwise let tests pass on broken pages.
    /// </summary>
    public static async Task AssertNoBlazorErrorAsync(this IPage page)
    {
        var errorUi = page.Locator("#blazor-error-ui");
        if (await errorUi.IsVisibleAsync())
        {
            var text = await errorUi.InnerTextAsync();
            Assert.Fail($"Blazor error UI was visible: {text}");
        }
    }
}
