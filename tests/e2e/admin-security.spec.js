import { csrf } from './admin-token.js';
import { expect, test } from '@playwright/test';

test('admin language labels render untrusted values as text', async ({ page }) => {
  const login = await page.request.post('/admin/login', {
    headers: await csrf(page.request),
    form: { name: 'BrowserAdmin', password: 'browser-pass' }
  });
  expect(login.ok()).toBeTruthy();
  const language = '<img src=x onerror="window.__review_xss=true">';
  await page.route('**/admin/rooms', (route) =>
    route.fulfill({
      json: [
        {
          RoomId: 'AAAAAAAAAAAAAAAAAAAAAQ',
          Name: 'Untrusted label',
          Language: language,
          UsersCount: 0,
          LastUpdatedUtc: '2026-10-19T00:00:00Z',
          CreatedBy: 'BrowserAdmin',
          CanDelete: true
        }
      ]
    })
  );

  await page.goto('/admin');

  await expect(page.locator('#rooms .badge')).toHaveText(language);
  await expect(page.locator('#rooms img')).toHaveCount(0);
  expect(await page.evaluate(() => window.__review_xss)).toBeUndefined();
});
