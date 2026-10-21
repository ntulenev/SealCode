import { test as base, expect } from '@playwright/test';
import { csrf } from './admin-token.js';

// Authenticate once per worker so collaboration tests do not consume login budgets.
export const test = base.extend({
  adminState: [
    async ({ playwright }, use, workerInfo) => {
    const request = await playwright.request.newContext({
      baseURL: workerInfo.project.use.baseURL
    });
    try {
      const response = await request.post('/admin/login', {
        headers: await csrf(request),
        form: { name: 'BrowserAdmin', password: 'browser-pass' }
      });
      expect(response.ok()).toBeTruthy();
      await use(await request.storageState());
    } finally {
      await request.dispose();
    }
    },
    { scope: 'worker' }
  ],
  storageState: async ({ adminState }, use) => use(adminState)
});

export { expect };
