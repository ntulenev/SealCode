import { test, expect } from './fixtures.js';
import { csrf } from './admin-token.js';

test('admin greeting and own-room filter follow the signed-in account', async ({ page, playwright, isolatedHost }, testInfo) => {
  const host = await isolatedHost({
    AdminUsers: [
      { Name: 'BrowserAdmin', Password: 'browser-pass', IsSuperAdmin: true },
      { Name: 'OtherAdmin', Password: 'other-pass' }
    ]
  });
  const other = await playwright.request.newContext({ baseURL: host.baseURL });
  try {
    await other.post('/admin/login', {
      headers: await csrf(other),
      form: { name: 'OtherAdmin', password: 'other-pass' }
    });
    const created = await other.post('/admin/rooms', {
      headers: await csrf(other), data: { name: 'Other room', language: 'sql' }
    });
    expect(created.ok()).toBeTruthy();
    await page.goto(`${host.baseURL}/admin/login`);
    await page.locator('input[name="name"]').fill('BrowserAdmin');
    await page.locator('input[name="password"]').fill('browser-pass');
    await page.locator('button[type="submit"]').click();
    await expect(page.locator('#adminGreeting')).toHaveText('Hello, BrowserAdmin!');
    const filter = page.getByRole('checkbox', { name: 'Only my rooms' });
    await expect(filter).toBeChecked();
    await expect(page.locator('#rooms')).toHaveText("You haven't created any rooms yet.");

    await page.locator('#roomName').fill('My room');
    await page.locator('#roomLanguage').selectOption('csharp');
    await page.locator('#createRoom').click();
    await expect(page.locator('#rooms .room-row')).toHaveCount(1);
    await expect(page.locator('#rooms strong')).toHaveText(['My room']);
    await expect(filter).toBeChecked();
    const roomLink = page.getByRole('link', { name: 'My room', exact: true });
    const roomPath = await roomLink.getAttribute('href');
    const popupPromise = page.waitForEvent('popup');
    await roomLink.click();
    const meeting = await popupPromise;
    await expect(meeting).toHaveURL(`${host.baseURL}${roomPath}`);
    await expect(meeting.locator('#joinOverlay')).toBeVisible();
    await expect(page).toHaveURL(`${host.baseURL}/admin`);
    await meeting.close();
    await filter.uncheck();
    await expect(page.locator('#rooms .room-row')).toHaveCount(2);
    await filter.focus();
    await page.keyboard.press('Space');
    await expect(page.locator('#rooms strong')).toHaveText(['My room']);
    for (const theme of ['dark', 'light']) {
      if (theme === 'light') await page.locator('[data-theme-toggle]').click();
      for (const width of [1440, 375]) {
        await page.setViewportSize({ width, height: 900 });
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
        await page.screenshot({ path: testInfo.outputPath(`admin-${theme}-${width}.png`), fullPage: true });
      }
    }
    page.once('dialog', (dialog) => dialog.accept());
    await page.getByRole('button', { name: 'Delete', exact: true }).click();
    await expect(page.locator('#rooms')).toHaveText("You haven't created any rooms yet.");
    await filter.uncheck();
    await expect(page.locator('#rooms strong')).toHaveText(['Other room']);

    await page.getByRole('button', { name: 'Logout' }).click();
    await page.locator('input[name="name"]').fill('OtherAdmin');
    await page.locator('input[name="password"]').fill('other-pass');
    await page.locator('button[type="submit"]').click();
    await expect(page.locator('#adminGreeting')).toHaveText('Hello, OtherAdmin!');
    await expect(page.getByRole('checkbox', { name: 'Only my rooms' })).toBeChecked();
    await expect(page.locator('#rooms strong')).toHaveText(['Other room']);
  } finally {
    await other.dispose();
  }
});
