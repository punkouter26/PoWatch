import { expect, test } from '@playwright/test';

test('archives page shows Generate Brief button when handoff coach enabled', async ({ page }) => {
  await page.goto('/archives', { waitUntil: 'networkidle' });

  await expect(page.getByRole('heading', { name: 'Archives' })).toBeVisible({ timeout: 15_000 });

  // The Generate Brief button should be visible (feature flag is on by default)
  const generateButton = page.getByRole('button', { name: /Generate Brief/i });
  await expect(generateButton).toBeVisible({ timeout: 10_000 });
});

test('archives page generates a handoff brief on button click', async ({ page }) => {
  await page.goto('/archives', { waitUntil: 'networkidle' });

  await expect(page.getByRole('heading', { name: 'Archives' })).toBeVisible({ timeout: 15_000 });

  const generateButton = page.getByRole('button', { name: /Generate Brief/i });
  await expect(generateButton).toBeVisible({ timeout: 10_000 });
  await expect(generateButton).toBeEnabled();

  await generateButton.click();

  // Brief panel should appear once the API returns a response
  const briefPanel = page.locator('.handoff-brief-panel');
  await expect(briefPanel).toBeVisible({ timeout: 30_000 });

  // Panel should contain the "Handoff Brief" heading
  await expect(briefPanel.getByRole('heading', { name: 'Handoff Brief' })).toBeVisible();

  // Summary text should be non-empty
  const summaryText = briefPanel.locator('.brief-summary');
  await expect(summaryText).not.toBeEmpty();
});

test('archives handoff brief panel can be dismissed', async ({ page }) => {
  await page.goto('/archives', { waitUntil: 'networkidle' });
  await expect(page.getByRole('heading', { name: 'Archives' })).toBeVisible({ timeout: 15_000 });

  const generateButton = page.getByRole('button', { name: /Generate Brief/i });
  await generateButton.click();

  const briefPanel = page.locator('.handoff-brief-panel');
  await expect(briefPanel).toBeVisible({ timeout: 30_000 });

  // Click the close (×) button
  const closeButton = briefPanel.locator('.brief-close');
  await closeButton.click();

  // Panel should be gone
  await expect(briefPanel).not.toBeVisible();
});

test('archives handoff brief shows source notes section', async ({ page }) => {
  await page.goto('/archives', { waitUntil: 'networkidle' });
  await expect(page.getByRole('heading', { name: 'Archives' })).toBeVisible({ timeout: 15_000 });

  await page.getByRole('button', { name: /Generate Brief/i }).click();

  const briefPanel = page.locator('.handoff-brief-panel');
  await expect(briefPanel).toBeVisible({ timeout: 30_000 });

  // Source notes details/summary element should be present
  await expect(briefPanel.locator('.brief-source-notes')).toBeVisible();
});

test('archives page loads without errors', async ({ page }) => {
  const errors: string[] = [];
  page.on('console', msg => {
    if (msg.type() === 'error') errors.push(msg.text());
  });
  page.on('pageerror', err => errors.push(err.message));

  await page.goto('/archives', { waitUntil: 'networkidle' });
  await expect(page.getByRole('heading', { name: 'Archives' })).toBeVisible({ timeout: 15_000 });

  const fatalErrors = errors.filter(e =>
    !e.includes('net::ERR_') &&
    !e.includes('Failed to fetch')
  );
  expect(fatalErrors).toHaveLength(0);
});
