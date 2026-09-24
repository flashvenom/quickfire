import { defineConfig, devices } from '@playwright/test';

// Playwright's automatic ARIA failure snapshot includes textbox values, including
// a briefly displayed helper credential. The pinned runner supports this switch.
process.env.PLAYWRIGHT_NO_COPY_PROMPT = '1';

export default defineConfig({
  testDir: '.',
  testMatch: '**/*.spec.ts',
  timeout: 90_000,
  expect: { timeout: 20_000 },
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: 'list',
  use: {
    ...devices['Desktop Chrome'],
    viewport: { width: 1440, height: 1000 },
    actionTimeout: 15_000,
    // The pairing test briefly displays a credential. Never capture it in artifacts.
    screenshot: 'off',
    trace: 'off',
    video: 'off',
  },
});
