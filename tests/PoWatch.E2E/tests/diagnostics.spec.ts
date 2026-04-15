import { expect, test } from '@playwright/test';

test('diagnostics page shows masked health details', async ({ page }) => {
  await page.goto('/diagnostics', { waitUntil: 'networkidle' });

  await expect(page.getByRole('heading', { name: 'System Diagnostics' })).toBeVisible({ timeout: 15000 });
  await expect(page.getByRole('button', { name: 'Refresh Diagnostics' })).toBeVisible();

  await expect(page.getByText('Masked Endpoint')).toBeVisible();
  await expect(page.getByText('Masked Key')).toBeVisible();
  await expect(page.locator('body')).toContainText('...');
  await expect(page.locator('body')).not.toContainText('DEV-LOCAL-KEY-12345');
});