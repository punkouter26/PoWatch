import { expect, test } from '@playwright/test';

test('identity nexus supports rename and merge revision flow', async ({ page }) => {
  await page.goto('/', { waitUntil: 'networkidle' });

  for (let index = 0; index < 3; index++) {
    await page.getByRole('button', { name: 'Inject Clinical Outlier' }).click();
    await expect(page.locator('.status-line')).toContainText(/Observation|recorded/i, { timeout: 15000 });
  }

  await page.goto('/identity', { waitUntil: 'networkidle' });

  await expect(page.getByRole('heading', { name: 'Identity Nexus' })).toBeVisible({ timeout: 15000 });
  await expect(page.getByText('Subject Library')).toBeVisible();
  await expect.poll(async () => await page.locator('table tbody tr').count()).toBeGreaterThan(1);

  const renameInputs = page.getByPlaceholder('Rename subject');
  await renameInputs.first().fill('Maya');
  await page.getByRole('button', { name: 'Save Rename' }).first().click();

  await expect(page.locator('.status-line')).toContainText(/Maya|Revision committed/i, { timeout: 15000 });
  await expect(page.getByRole('button', { name: 'Commit Merge' })).toBeEnabled({ timeout: 15000 });

  await page.getByRole('button', { name: 'Commit Merge' }).click();
  await expect(page.locator('.status-line')).toContainText(/Merge committed|Revision committed/i, { timeout: 15000 });
});
