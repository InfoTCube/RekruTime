import { defineConfig, devices } from '@playwright/test'
import { config } from './config.mjs'

// Browser tests of the whole recruitment flow against the real app (see server.mjs). Specs share one database,
// so they run one after another and each uses its own names (run id) – see tests/helpers.ts.
export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  timeout: 60_000,
  expect: { timeout: 10_000 },
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : [['list']],
  use: {
    baseURL: config.baseURL,
    locale: 'en-GB',
    timezoneId: 'Europe/Warsaw', // the club's zone, so times on screen match the e-mails
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    // Locally the installed Chrome is used (no browser download); CI installs Playwright's Chromium.
    { name: 'chrome', use: { ...devices['Desktop Chrome'], channel: process.env.CI ? undefined : 'chrome' } },
  ],
  webServer: {
    command: 'node server.mjs',
    url: `${config.baseURL}/api/health`,
    timeout: 180_000,
    reuseExistingServer: false,
    stdout: 'ignore',
    stderr: 'pipe',
  },
})
