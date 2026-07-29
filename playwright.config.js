import { defineConfig } from '@playwright/test';

const baseURL = 'http://127.0.0.1:5087';

export default defineConfig({
  testDir: './tests/e2e',
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
    command: `dotnet run --project src/SealCode/SealCode.csproj --no-launch-profile --no-build --urls ${baseURL}`,
    url: `${baseURL}/health`,
    reuseExistingServer: false,
    timeout: 120_000,
    env: {
      ...process.env,
      AdminUsers__0__Name: 'BrowserAdmin',
      AdminUsers__0__Password: 'browser-pass',
      AdminUsers__0__IsSuperAdmin: 'true',
      Languages__0: 'csharp',
      Languages__1: 'sql',
      MaxUsersPerRoom: '5'
    }
  }
});
