import { expect, test } from '@playwright/test';

test('observer hub and sector navigation are visible', async ({ page }) => {
  await page.goto('/', { waitUntil: 'networkidle' });

  await expect(page.getByRole('heading', { name: 'Observer Hub' })).toBeVisible({ timeout: 15000 });
  await expect(page.getByRole('link', { name: 'Archives' })).toBeVisible();

  await page.getByRole('link', { name: 'Diagnostics' }).click();
  await expect(page.getByRole('heading', { name: 'System Diagnostics' })).toBeVisible();
});
