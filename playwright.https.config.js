import { testHostCommand } from './tests/test-host.js';
import { defineConfig } from '@playwright/test';
import { pbkdf2Sync, randomBytes } from 'node:crypto';
const salt = randomBytes(16); const header = Buffer.alloc(13); header[0] = 1;
header.writeUInt32BE(2, 1); header.writeUInt32BE(100000, 5); header.writeUInt32BE(16, 9);
// Public disposable test credentials; production secrets never enter this fixture.
const hash = Buffer.concat([header, salt, pbkdf2Sync('browser-pass', salt, 100000, 32, 'sha512')]).toString('base64');
export default defineConfig({
  testDir: './tests/https', outputDir: 'test-results/https', workers: 1, use: { baseURL: 'https://localhost:5088', ignoreHTTPSErrors: true, trace: 'retain-on-failure' },
  webServer: {
    command: testHostCommand('https://localhost:5088', [{ Name: 'BrowserAdmin', PasswordHash: hash, IsSuperAdmin: true }]),
    url: 'https://localhost:5088/health', ignoreHTTPSErrors: true, reuseExistingServer: false,
    env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Production' }
  }
});
