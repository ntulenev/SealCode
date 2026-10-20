import { csrf } from './admin-token.js';
import { expect, test } from '@playwright/test';

test('every page displays the deployed version and commit from the server', async ({ page }) => {
  const response = await page.request.get('/version');
  expect(response.headers()['cache-control']).toBe('no-store');
  const version = await response.json();
  const expected = `SealCode v${version.Version} · ${version.Commit.slice(0, 7)}`;
  expect((await page.request.post('/admin/login', {
    headers: await csrf(page.request),
    form: { name: 'BrowserAdmin', password: 'browser-pass' }
  })).ok()).toBeTruthy();
  const room = await page.request.post('/admin/rooms', {
    headers: await csrf(page.request),
    data: { name: 'Version footer', language: 'csharp' }
  });
  const roomId = (await room.json()).RoomId;

  for (const path of ['/', '/about.html', '/admin/login', '/admin', `/room/${roomId}`]) {
    await page.goto(path);
    await expect(page.locator('[data-app-version]')).toHaveText(expected);
    await expect(page.locator('[data-app-version]')).toHaveAttribute('title', version.InformationalVersion);
  }
});

test('version fetch failures leave the page usable without showing a false version', async ({ page }) => {
  await page.route('**/version', (route) => route.fulfill({ status: 503 }));
  await page.goto('/');
  await expect(page.locator('[data-app-version]')).toHaveText('SealCode · version unavailable');
  await expect(page.locator('#goBtn')).toBeEnabled();
});
