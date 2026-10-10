import { defineConfig } from '@playwright/test';
export default defineConfig({
  testDir: '.',
  testMatch: '*.spec.mjs',
  timeout: 240_000,
  expect: { timeout: 20_000 },
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: process.env.REPORTSCENE_URL || 'http://127.0.0.1:8000/ReportScene/',
    viewport: { width: 1600, height: 1000 },
    deviceScaleFactor: 1,
    browserName: 'chromium',
    launchOptions: { args: ['--enable-webgl', '--ignore-gpu-blocklist', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'] },
    acceptDownloads: true,
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure'
  }
});
