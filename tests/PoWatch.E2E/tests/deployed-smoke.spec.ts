import { expect, test } from '@playwright/test';

const liveDashboardPath = '/live-dashboard';
const archivesPath = '/archives';
const identityPath = '/identity';
const diagnosticsPath = '/diagnostics';

test.describe('deployed smoke regression checklist', () => {
  test('checks key routes and non-destructive interactions', async ({ page }) => {
    await page.goto('/', { waitUntil: 'networkidle' });
    await expect(page.getByRole('heading', { name: /Observer Hub/i })).toBeVisible({ timeout: 20000 });

    await page.goto(diagnosticsPath, { waitUntil: 'networkidle' });
    await expect(page.getByRole('heading', { name: /System Diagnostics/i })).toBeVisible({ timeout: 20000 });
    const capturedBefore = await page.getByText(/Captured/i).locator('..').innerText();
    await page.getByRole('button', { name: /Refresh Now/i }).click();
    await expect.poll(async () => {
      return page.getByText(/Captured/i).locator('..').innerText();
    }).not.toEqual(capturedBefore);

    await page.goto(liveDashboardPath, { waitUntil: 'networkidle' });
    await expect(page.getByRole('heading', { name: /Live Dashboard/i })).toBeVisible({ timeout: 20000 });
    const filter = page.getByRole('textbox', { name: /Filter subjects by name or activity/i });
    await filter.fill('Subject');
    await expect(filter).toHaveValue('Subject');

    await page.goto(archivesPath, { waitUntil: 'networkidle' });
    await expect(page.getByRole('heading', { name: /^Archives$/i })).toBeVisible({ timeout: 20000 });
    await expect(page.getByRole('button', { name: /Handoff|Generate handoff/i })).toBeVisible();

    await page.goto(identityPath, { waitUntil: 'networkidle' });
    await expect(page.getByRole('heading', { name: /Identity Nexus/i })).toBeVisible({ timeout: 20000 });

    const subjectInput = page.getByRole('textbox', { name: /New subject display name/i });
    await subjectInput.fill('Smoke Subject Candidate');
    const addButton = page.getByRole('button', { name: /Add Known Subject/i });
    await expect(addButton).toBeEnabled();

    // Leave no typed state behind.
    await subjectInput.fill('');
    await expect(addButton).toBeDisabled();
  });
});
