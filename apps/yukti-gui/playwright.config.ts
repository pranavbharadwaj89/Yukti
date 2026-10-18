import { defineConfig, devices } from '@playwright/test';

/**
 * Playwright E2E configuration for Yukti Platform GUI
 * See https://playwright.dev/docs/test-configuration.
 *
 * ADR-0005: these tests drive the real stack — the API (with `Ai:Provider=fixture`, so no live
 * AI call) on :5000 against a real Postgres/Redis, plus the Vite dev server below. The API must
 * already be running; CI's `e2e` job starts it. `npm run test:e2e` selects Chromium.
 */
export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  // No retries: a flow that only passes on a retry is flaky, and an e2e harness that quietly
  // retries hides exactly that (CLAUDE.md principle 6 — honest reporting).
  retries: 0,
  workers: process.env.CI ? 1 : undefined,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'html',
  use: {
    baseURL: 'http://localhost:5173',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
    {
      name: 'firefox',
      use: { ...devices['Desktop Firefox'] },
    },
    {
      name: 'webkit',
      use: { ...devices['Desktop Safari'] },
    },
  ],
  webServer: {
    command: 'npm run dev',
    url: 'http://localhost:5173',
    reuseExistingServer: !process.env.CI,
  },
});
