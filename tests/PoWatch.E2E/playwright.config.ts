import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './tests',
  timeout: 30_000,
  use: {
    baseURL: process.env.POWATCH_CLIENT_URL ?? 'http://localhost:5000',
    headless: process.env.CI === 'true',
    trace: 'on-first-retry',
    permissions: ['camera'],
    launchOptions: {
      args: ['--use-fake-ui-for-media-stream', '--use-fake-device-for-media-stream']
    }
  }
});
