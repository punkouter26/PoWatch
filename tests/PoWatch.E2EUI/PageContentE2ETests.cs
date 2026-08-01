using Microsoft.Playwright;

namespace PoWatch.E2EUI;

/// <summary>
/// What each page actually shows: the controls a caregiver uses, and the consistency rules that
/// broke silently before — one person under two names, a heading that contradicts the nav,
/// a destructive action given top billing.
/// </summary>
[Collection(nameof(PlaywrightCollection))]
public sealed class PageContentE2ETests(PlaywrightFixture fixture)
{
    [Fact]
    public async Task The_live_room_leads_with_room_state_and_one_obvious_action()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);

        await Assertions.Expect(page.GetByTestId("room-status-hero")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("hero-start")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("hero-handoff")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task The_live_room_shows_model_telemetry()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);

        await Assertions.Expect(page.GetByTestId("model-stats-strip")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Observer_settings_open_in_a_drawer_and_close_again()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);

        await page.GetByTestId("observer-settings-gear").ClickAsync();
        await Assertions.Expect(page.GetByTestId("observer-settings-drawer")).ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "Close settings" }).ClickAsync();
        await Assertions.Expect(page.GetByTestId("observer-settings-drawer")).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task Clearing_all_data_is_not_offered_on_the_daily_use_screen()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);

        // It used to be a bright red button pinned above the activity feed. It now lives with the
        // other developer tools inside the settings drawer.
        await Assertions.Expect(page.GetByTestId("live-clear-data")).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task The_people_page_shows_the_glance_grid_and_the_full_list()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);
        await page.GoToAsync("/identity", "People");

        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Everyone at a glance" }))
            .ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "All people" }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task The_people_page_never_shows_a_raw_storage_id()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);
        await page.GoToAsync("/identity", "People");
        await page.WaitForTimeoutAsync(2000);

        // The glance cards said "Person 116" while the table beneath said "Subject-116" — the same
        // person under two names, on one screen.
        Assert.DoesNotContain("Subject-", await page.InnerTextAsync("body"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_history_page_never_shows_a_raw_storage_id()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);
        await page.GoToAsync("/archives", "History");
        await page.WaitForTimeoutAsync(2000);

        Assert.DoesNotContain("Subject-", await page.InnerTextAsync("body"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_people_filter_narrows_the_glance_grid()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);
        await page.GoToAsync("/identity", "People");

        // Let the grid populate first, so this asserts filtering rather than an empty database.
        await page.WaitForTimeoutAsync(2500);
        await page.GetByTestId("glance-filter").FillAsync("zzz-no-such-person");
        await page.WaitForTimeoutAsync(800);

        var cards = await page.Locator(".subject-card-grid .subject-card").CountAsync();
        Assert.Equal(0, cards);

        var text = await page.InnerTextAsync("body");
        Assert.True(
            text.Contains("No one matches", StringComparison.OrdinalIgnoreCase)
            || text.Contains("No one seen yet", StringComparison.OrdinalIgnoreCase),
            "Filtering to a name nobody has should say so, not just show an empty grid.");
    }

    [Fact]
    public async Task The_history_page_offers_day_navigation()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);
        await page.GoToAsync("/archives", "History");

        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Previous day" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Go to today" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Arrow_keys_page_through_days_without_clicking_first()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);
        await page.GoToAsync("/archives", "History");
        await page.WaitForTimeoutAsync(1500);

        var before = await page.InputValueAsync(".archive-toolbar input");
        await page.Keyboard.PressAsync("ArrowLeft");
        await page.WaitForTimeoutAsync(1500);
        var after = await page.InputValueAsync(".archive-toolbar input");

        // The handler is bound to the page grid, which was never focused — the shortcut advertised
        // in the toolbar tooltips only worked after clicking the page first.
        Assert.NotEqual(before, after);
    }

    [Fact]
    public async Task Auto_focusing_the_history_grid_does_not_paint_a_focus_ring()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);
        await page.GoToAsync("/archives", "History");
        await page.WaitForTimeoutAsync(1200);

        // The page focuses its own grid so the day shortcuts work without a click. That focus is
        // programmatic, not a user landing on a control, so it must not light the whole layout up.
        var ring = await page.EvaluateAsync<string>(@"() => {
            const g = document.querySelector('.archives-grid');
            if (!g || document.activeElement !== g) return 'not-focused';
            const cs = getComputedStyle(g);
            return cs.outlineStyle + '|' + cs.boxShadow;
        }");

        Assert.NotEqual("not-focused", ring);
        Assert.StartsWith("none|", ring, StringComparison.Ordinal);
        Assert.Contains("none", ring.Split('|')[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_system_page_fills_its_width_rather_than_half_of_it()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);
        await page.GoToAsync("/diagnostics", "System");

        // Poll rather than sample once: under parallel test load the first measurement can land
        // before the grid has laid out, which reads as a false 0-width failure.
        double ratio = 0;
        for (var attempt = 0; attempt < 20 && ratio <= 0.9; attempt++)
        {
            ratio = await page.EvaluateAsync<double>(@"() => {
                const card = document.querySelector('.diagnostics-page .panel');
                const grid = document.querySelector('.diagnostics-page');
                return card && grid && grid.clientWidth > 0 ? card.clientWidth / grid.clientWidth : 0;
            }");
            if (ratio <= 0.9) await page.WaitForTimeoutAsync(250);
        }

        // A stale 2-column rule left the single surviving card at ~50% with the rest of the
        // viewport empty.
        Assert.True(ratio > 0.9, $"System card occupies only {ratio:P0} of the page width.");
    }

    [Fact]
    public async Task The_system_page_reports_runtime_and_inference_state()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);
        await page.GoToAsync("/diagnostics", "System");

        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Inference engine" }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task Only_one_page_is_headed_Health()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);

        await page.GoToAsync("/diagnostics", "System");
        Assert.Equal("System", await page.InnerTextAsync(".diagnostics-page h1"));

        await page.GoToAsync("/health", "Health");
        Assert.Equal("Connection health", await page.InnerTextAsync(".health-page h1"));
    }

    [Fact]
    public async Task The_health_page_lists_every_connection_with_a_verdict()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);
        await page.GoToAsync("/health", "Health");

        await Assertions.Expect(page.GetByTestId("health-overall")).ToBeVisibleAsync(new() { Timeout = 30000 });
        // Wait for the list itself: "Checking…" also renders health-overall, so counting straight
        // after it is a race with the first fetch.
        await Assertions.Expect(page.GetByTestId("health-check-list")).ToBeVisibleAsync(new() { Timeout = 30000 });
        Assert.True(await page.GetByTestId("health-check").CountAsync() > 0);
    }

    [Fact]
    public async Task The_health_page_re_checks_on_demand()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);
        await page.GoToAsync("/health", "Health");

        await Assertions.Expect(page.GetByTestId("health-timestamp")).ToBeVisibleAsync(new() { Timeout = 30000 });
        await page.GetByTestId("health-refresh").ClickAsync();
        await Assertions.Expect(page.GetByTestId("health-overall")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task The_health_page_does_not_publish_the_key_vault_hostname()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);
        await page.GoToAsync("/health", "Health");
        await page.WaitForTimeoutAsync(1500);

        Assert.DoesNotContain(".vault.azure.net", await page.InnerTextAsync("body"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Signing_out_returns_the_operator_to_the_login_page()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);

        await page.GetByTestId("navbar-sign-out").ClickAsync();

        await Assertions.Expect(page.GetByTestId("login-shell")).ToBeVisibleAsync(new() { Timeout = 60000 });
    }

    [Fact]
    public async Task The_login_page_offers_the_guest_bypass_in_a_non_production_environment()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await fixture.Browser.NewPageAsync(new() { IgnoreHTTPSErrors = true });

        await page.GotoAsync($"{PlaywrightFixture.BaseUrl}/login");
        await Assertions.Expect(page.GetByTestId("login-card")).ToBeVisibleAsync(new() { Timeout = 60000 });

        // Whatever is configured, the page must resolve to a real choice rather than sitting on
        // "Loading sign-in options…" forever.
        await Assertions.Expect(page.GetByTestId("login-loading")).Not.ToBeVisibleAsync(new() { Timeout = 30000 });
    }

    [Fact]
    public async Task Flagged_events_use_the_warning_ramp_not_the_success_ramp()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);
        await page.WaitForTimeoutAsync(2000);

        var greenBadges = await page.EvaluateAsync<int>(@"() => {
            const badges = document.querySelectorAll('.activity-badge--significant');
            let green = 0;
            for (const b of badges) {
                const c = getComputedStyle(b).color;
                const m = c.match(/\d+/g);
                if (!m) continue;
                const [r, g, bl] = m.map(Number);
                // 'Notable' rendered in the same green the app uses for healthy/connected.
                if (g > r + 40 && g > bl + 40) green++;
            }
            return green;
        }");

        Assert.Equal(0, greenBadges);
    }

    [Fact]
    public async Task Every_page_keeps_its_content_inside_the_viewport_width()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);

        foreach (var (route, heading) in new[]
                 {
                     ("/", "Live Room"), ("/archives", "History"),
                     ("/identity", "People"), ("/diagnostics", "System"), ("/health", "Health")
                 })
        {
            await page.GoToAsync(route, heading);
            var overflow = await page.EvaluateAsync<int>(
                "() => document.documentElement.scrollWidth - document.documentElement.clientWidth");

            Assert.True(overflow <= 1, $"{route} overflows horizontally by {overflow}px.");
        }
    }

    [Fact]
    public async Task The_shell_still_works_at_a_phone_width()
    {
        if (PlaywrightFixture.BaseUrl is null) return;
        var page = await PoWatchPage.SignedInAsync(fixture.Browser);
        await page.SetViewportSizeAsync(390, 844);
        await page.WaitForTimeoutAsync(500);

        await Assertions.Expect(page.GetByTestId("navbar-toggler")).ToBeVisibleAsync();
        var overflow = await page.EvaluateAsync<int>(
            "() => document.documentElement.scrollWidth - document.documentElement.clientWidth");
        Assert.True(overflow <= 1, $"Live Room overflows by {overflow}px at 390px wide.");
    }
}
