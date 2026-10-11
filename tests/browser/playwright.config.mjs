import { defineConfig } from '@playwright/test';
export default defineConfig({
  testDir: '.', testMatch: '*.spec.mjs',
  globalTeardown: './summarize-evidence.mjs',
  timeout: 420_000, expect: { timeout: 60_000 },
  // CI distributes independent tests across isolated machines. Each software-GPU runner
  // still runs only one browser worker, with no retries or waived assertions.
  fullyParallel: true, workers: 1, retries: 0,
  reporter: process.env.CI ? [['list'], ['blob']] : [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: process.env.REPORTSCENE_URL || 'http://127.0.0.1:8000/ReportScene/',
    viewport: { width: 1600, height: 1000 }, deviceScaleFactor: 1, browserName: 'chromium',
    launchOptions: { args: ['--enable-webgl', '--ignore-gpu-blocklist', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'] },
    acceptDownloads: true, screenshot: 'only-on-failure', trace: 'retain-on-failure'
  }
});
