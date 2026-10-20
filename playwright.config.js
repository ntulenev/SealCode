import { testHostCommand } from './tests/test-host.js';
import { defineConfig } from '@playwright/test';

const baseURL = 'http://127.0.0.1:5087';

export default defineConfig({
  testDir: './tests/e2e',
  outputDir: 'test-results/browser',
  fullyParallel: false,
  workers: 1,
  timeout: 60_000,
  expect: {
    timeout: 10_000
  },
  use: {
    baseURL,
    headless: true,
    trace: 'retain-on-failure'
  },
  webServer: {
    command: testHostCommand(baseURL, [
      { Name: 'BrowserAdmin', Password: 'browser-pass', IsSuperAdmin: true }
    ]),
    url: `${baseURL}/health`,
    reuseExistingServer: false,
    timeout: 120_000,
    env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Development' }
  }
});
