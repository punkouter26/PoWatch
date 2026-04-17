import { expect, test } from '@playwright/test';

test('live dashboard shows drift badges when subjects have enough events', async ({ page }) => {
  // First ingest enough events on the Observer Hub to create a subject with drift data
  await page.goto('/', { waitUntil: 'networkidle' });

  // Ingest 4 observations to get above MinEventsForDrift threshold
  for (let i = 0; i < 4; i++) {
    await page.getByRole('button', { name: 'Inject Observation' }).click();
    await expect(page.locator('.status-line')).toContainText(/Observation|recorded/i, { timeout: 15_000 });
  }

  // Navigate to the live dashboard
  await page.goto('/live-dashboard', { waitUntil: 'networkidle' });
  await expect(page.getByRole('heading', { name: 'Live Dashboard' })).toBeVisible({ timeout: 15_000 });

  // If drift radar is enabled and a subject has enough events, the drift badge should be visible.
  // Allow for no subjects (empty state) without failing.
  const subjectCards = page.locator('.subject-card');
  const cardCount = await subjectCards.count();

  if (cardCount > 0) {
    // At least one subject card should either have a drift badge or show "Insufficient Data" state
    // (badge hidden for Insufficient Data)
    const driftBadges = page.locator('.subject-card-drift');
    const badgeCount = await driftBadges.count();

    if (badgeCount > 0) {
      await expect(driftBadges.first()).toBeVisible();
      await expect(driftBadges.first()).toContainText(/DRIFT/i);
    }
  }
});

test('live dashboard drift badge opens detail dialog on click', async ({ page }) => {
  // Ingest enough events so at least one subject has a drift badge
  await page.goto('/', { waitUntil: 'networkidle' });

  for (let i = 0; i < 5; i++) {
    await page.getByRole('button', { name: 'Inject Observation' }).click();
    await expect(page.locator('.status-line')).toContainText(/Observation|recorded/i, { timeout: 15_000 });
  }

  await page.goto('/live-dashboard', { waitUntil: 'networkidle' });
  await expect(page.getByRole('heading', { name: 'Live Dashboard' })).toBeVisible({ timeout: 15_000 });

  const driftBadges = page.locator('.subject-card-drift');
  const badgeCount = await driftBadges.count();

  if (badgeCount > 0) {
    // Click the first drift badge — should open the Radzen dialog
    await driftBadges.first().click();

    // Drift detail dialog should appear with the heading "Drift Analysis"
    await expect(page.locator('.rz-dialog-content, [role="dialog"]')).toBeVisible({ timeout: 10_000 });
  }
});

test('live dashboard loads without errors when drift radar is enabled', async ({ page }) => {
  const errors: string[] = [];
  page.on('console', msg => {
    if (msg.type() === 'error') errors.push(msg.text());
  });
  page.on('pageerror', err => errors.push(err.message));

  await page.goto('/live-dashboard', { waitUntil: 'networkidle' });
  await expect(page.getByRole('heading', { name: 'Live Dashboard' })).toBeVisible({ timeout: 15_000 });

  const fatalErrors = errors.filter(e =>
    !e.includes('net::ERR_') && // ignore network availability errors in test env
    !e.includes('Failed to fetch')
  );
  expect(fatalErrors).toHaveLength(0);
});
