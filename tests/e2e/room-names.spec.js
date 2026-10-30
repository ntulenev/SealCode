import { test, expect } from './fixtures.js';
import { csrf } from './admin-token.js';

test('long room names survive creation and joining, wrap and expose their full text', async ({ page, request }, testInfo) => {
  const names = [
    'Собеседование с Иваном Ивановым — Senior .NET Developer — команда платформы — технический этап — 30 октября 2026',
    'Я'.repeat(160)
  ];
  for (const [index, name] of names.entries()) {
    await page.goto('/admin');
    const input = page.getByLabel('Room name (up to 160 characters)', { exact: true });
    await expect(input).toHaveAttribute('maxlength', '160');
    await input.fill(name);
    await page.locator('#roomLanguage').selectOption('csharp');
    const responsePromise = page.waitForResponse((response) =>
      response.url().endsWith('/admin/rooms') && response.request().method() === 'POST');
    await page.locator('#createRoom').click();
    const created = await responsePromise;
    expect(created.ok()).toBeTruthy();
    const room = await created.json();
    expect(room.Name).toBe(name);
    try {
      const title = page.locator('#rooms strong').filter({ hasText: name });
      await expect(title).toHaveText(name);
      await expect(title).toHaveAttribute('title', name);
      for (const width of [1440, 375]) {
        await page.setViewportSize({ width, height: 900 });
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
        await page.screenshot({ path: testInfo.outputPath(`admin-name-${index}-${width}.png`), fullPage: true });
      }
      await page.goto(`/room/${room.RoomId}`);
      await page.locator('#displayNameInput').fill('Reviewer');
      await page.locator('#joinBtn').click();
      await expect(page.locator('#joinOverlay')).toBeHidden();
      await expect(page.locator('#roomName')).toHaveText(name);
      await expect(page.locator('#roomName')).toHaveAttribute('title', name);
      for (const width of [1440, 375]) {
        await page.setViewportSize({ width, height: 900 });
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
        const heading = page.locator('#roomName');
        expect(await heading.evaluate((element) => element.scrollWidth <= element.clientWidth)).toBe(true);
        await page.screenshot({ path: testInfo.outputPath(`room-name-${index}-${width}.png`), fullPage: true });
      }
    } finally {
      await request.delete(`/admin/rooms/${room.RoomId}`, { headers: await csrf(request) });
    }
  }
  const rejected = await request.post('/admin/rooms', {
    headers: await csrf(request), data: { name: 'Я'.repeat(161), language: 'csharp' }
  });
  expect(rejected.status()).toBe(400);
});
