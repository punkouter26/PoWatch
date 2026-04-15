import { expect, test } from '@playwright/test';

test('observer hub monitoring controls update the status, live preview, and clinical stream', async ({ page }) => {
  await page.goto('/', { waitUntil: 'networkidle' });

  await expect(page.getByRole('heading', { name: 'Observer Hub' })).toBeVisible({ timeout: 15000 });

  await page.getByRole('button', { name: 'Start Monitoring' }).click();
  await expect(page.getByText(/Monitoring active|Inference Unavailable|Webcam access denied or unavailable/i)).toBeVisible();

  const livePreview = page.locator('video.live-camera-feed');
  await expect(livePreview).toBeVisible({ timeout: 15000 });
  await expect.poll(async () => {
    return livePreview.evaluate((video: HTMLVideoElement) => Boolean(video.srcObject) && video.readyState >= 2);
  }).toBeTruthy();

  await page.getByRole('button', { name: 'Inject Significant Event' }).click();
  await expect(page.getByText(/Observation recorded|No state change detected; redundant observation skipped/i)).toBeVisible({ timeout: 15000 });
  await expect(page.getByText(/Kim|Desk Work/).first()).toBeVisible({ timeout: 15000 });

  await page.getByRole('button', { name: 'Inject Clinical Outlier' }).click();
  await expect(page.locator('.status-line')).toContainText(/Clinical outlier recorded|Clinical outlier/i, { timeout: 15000 });
});
