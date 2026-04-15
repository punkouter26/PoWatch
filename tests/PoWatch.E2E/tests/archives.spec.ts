import { expect, test } from '@playwright/test';

test('archives page loads a chapter and shows empty-day or chapter content', async ({ page }) => {
  await page.goto('/archives', { waitUntil: 'networkidle' });

  await expect(page.getByRole('heading', { name: 'Archives' })).toBeVisible({ timeout: 15000 });
  await expect(page.getByRole('button', { name: 'Load Chapter' })).toBeVisible();

  await page.getByRole('button', { name: 'Load Chapter' }).click();

  await expect(page.locator('.narrative')).toContainText(/No data for this day|The room recorded/i, { timeout: 15000 });
});
