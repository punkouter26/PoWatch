/**
 * PoRun Ad-lib Discovery Test — NOT a permanent test.
 * Created by PoRun prompt for on-the-fly UI inspection.
 * Run: npx playwright test porun-adlib.spec.ts --headed
 */
import { test, expect } from '@playwright/test';
import path from 'path';
import fs from 'fs';

const screenshotDir = path.join(__dirname, '..', 'test-results', 'porun-screenshots');

test.beforeAll(() => {
  if (!fs.existsSync(screenshotDir)) {
    fs.mkdirSync(screenshotDir, { recursive: true });
  }
});

test.describe('PoRun: UI Discovery Sweep', () => {

  test('01 - Home / ObserverHub loads and renders metrics', async ({ page }) => {
    await page.goto('/', { waitUntil: 'networkidle' });
    await page.screenshot({ path: path.join(screenshotDir, '01-observer-hub.png'), fullPage: true });

    // Check hero section
    await expect(page.locator('h1')).toBeVisible();
    const h1Text = await page.locator('h1').first().textContent();
    expect(h1Text).toContain('Observer Hub');

    // Verify status/model/latest-activity metrics
    const metricCards = page.locator('.metric-card');
    const metricCount = await metricCards.count();
    expect(metricCount).toBeGreaterThanOrEqual(3);

    // Check HUD is visible (EnableHud=true in dev)
    const hud = page.locator('.hud-bar');
    const hudVisible = await hud.isVisible();
    console.log(`HUD visible: ${hudVisible}`);
    if (hudVisible) {
      await page.screenshot({ path: path.join(screenshotDir, '01b-observer-hub-hud.png'), fullPage: false });
    }
  });

  test('02 - ObserverHub camera feed and controls visible', async ({ page }) => {
    await page.goto('/', { waitUntil: 'networkidle' });

    // Camera feed container
    const videoEl = page.locator('video.live-camera-feed');
    await expect(videoEl).toBeVisible();

    // Monitoring control buttons
    const startBtn = page.locator('button').filter({ hasText: /start|begin|activate/i });
    const stopBtn = page.locator('button').filter({ hasText: /stop|halt|deactivate/i });
    const startCount = await startBtn.count();
    const stopCount = await stopBtn.count();
    console.log(`Start-like buttons: ${startCount}, Stop-like buttons: ${stopCount}`);

    await page.screenshot({ path: path.join(screenshotDir, '02-observer-controls.png'), fullPage: true });
  });

  test('03 - Archives page loads and displays date navigation', async ({ page }) => {
    await page.goto('/archives', { waitUntil: 'networkidle' });
    await page.screenshot({ path: path.join(screenshotDir, '03-archives.png'), fullPage: true });

    const heading = page.locator('h1, h2').first();
    await expect(heading).toBeVisible();
    const headingText = await heading.textContent();
    console.log(`Archives heading: ${headingText}`);

    // Check for date picker or navigation
    const datePicker = page.locator('input[type=date], .rz-datepicker, [class*="date"]').first();
    const datePickerVisible = await datePicker.isVisible().catch(() => false);
    console.log(`Date picker visible: ${datePickerVisible}`);
  });

  test('04 - IdentityNexus page loads', async ({ page }) => {
    await page.goto('/identity', { waitUntil: 'networkidle' });
    await page.screenshot({ path: path.join(screenshotDir, '04-identity-nexus.png'), fullPage: true });

    const heading = page.locator('h1, h2').first();
    await expect(heading).toBeVisible();
    const headingText = await heading.textContent();
    console.log(`Identity heading: ${headingText}`);
  });

  test('05 - Diagnostics page loads and shows system state', async ({ page }) => {
    await page.goto('/diagnostics', { waitUntil: 'networkidle' });
    await page.screenshot({ path: path.join(screenshotDir, '05-diagnostics.png'), fullPage: true });

    await expect(page.locator('h1').first()).toContainText('Diagnostics');

    // Refresh button should be visible
    const refreshBtn = page.locator('button').filter({ hasText: /refresh/i });
    await expect(refreshBtn).toBeVisible();

    // Verify auto-refresh toggle
    const autoRefreshToggle = page.locator('.rz-switch, [class*="switch"]').first();
    const toggleVisible = await autoRefreshToggle.isVisible().catch(() => false);
    console.log(`Auto-refresh toggle visible: ${toggleVisible}`);

    // Check diagnostics data loaded (grid should have items)
    const gridItems = page.locator('.hud-grid div, .diagnostics-grid div');
    const gridCount = await gridItems.count();
    console.log(`Diagnostic grid items: ${gridCount}`);
    await page.screenshot({ path: path.join(screenshotDir, '05b-diagnostics-data.png'), fullPage: true });
  });

  test('06 - Nav links function and breadcrumbs correct', async ({ page }) => {
    await page.goto('/', { waitUntil: 'networkidle' });

    // Find nav links
    const navLinks = page.locator('nav a, .nav a, aside a, .sidebar a');
    const linkCount = await navLinks.count();
    console.log(`Nav links found: ${linkCount}`);

    // Click Archives nav link
    const archivesLink = page.locator('a').filter({ hasText: /archive/i }).first();
    const archivesLinkVisible = await archivesLink.isVisible().catch(() => false);
    if (archivesLinkVisible) {
      await archivesLink.click();
      await page.waitForURL('**/archives', { timeout: 5000 }).catch(() => {});
      await page.screenshot({ path: path.join(screenshotDir, '06-nav-archives.png'), fullPage: false });
    }
    await page.screenshot({ path: path.join(screenshotDir, '06-nav-overview.png'), fullPage: true });
  });

  test('07 - API state endpoint returns valid data', async ({ page }) => {
    const response = await page.request.get('/api/observer/state');
    expect(response.status()).toBe(200);
    const body = await response.json();
    console.log(`Observer state: ${JSON.stringify(body, null, 2)}`);
    // DTO uses 'status' string ("Idle" | "Running" | ...), not a boolean isRunning
    expect(body).toHaveProperty('status');
    expect(body).toHaveProperty('observationLoopEnabled');
  });

  test('08 - API archives endpoint returns valid data', async ({ page }) => {
    const today = new Date();
    today.setDate(today.getDate() + 1); // archives query uses +1 in logs
    const dateStr = today.toISOString().split('T')[0];
    const response = await page.request.get(`/api/archives/${dateStr}`);
    expect(response.status()).toBe(200);
    const body = await response.json();
    console.log(`Archives response keys: ${Object.keys(body).join(', ')}`);
  });

  test('09 - HTTPS redirect warning noted (non-blocking)', async ({ page }) => {
    // The log shows: "Failed to determine the https port for redirect"
    // Verify site loads over HTTP without redirect loop
    const response = await page.goto('http://localhost:5000/', { waitUntil: 'domcontentloaded' });
    expect(response?.status()).toBeLessThan(400);
    console.log(`HTTP homepage status: ${response?.status()}`);
    await page.screenshot({ path: path.join(screenshotDir, '09-http-load.png'), fullPage: false });
  });

  test('10 - Anonymous auth check: observer state accessible without login', async ({ page }) => {
    // DeveloperBypassAuth=true but requests show [anonymous] - verify behavior
    const response = await page.request.get('/api/observer/state');
    const status = response.status();
    console.log(`Observer state HTTP status (no auth header): ${status}`);
    // Should be 200 with DeveloperBypassAuth enabled
    expect(status).toBe(200);
    await page.screenshot({ path: path.join(screenshotDir, '10-auth-state.png'), fullPage: false });
  });
});
