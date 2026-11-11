import { test, expect } from './fixtures.js';
import { csrf } from './admin-token.js';

test('super admin can inspect usage and user history and cancel retention', async ({ page, request }, testInfo) => {
  const name = `Statistics room ${Date.now()}`;
  const created = await request.post('/admin/rooms', { headers: await csrf(request), data: { name, language: 'csharp' } });
  expect(created.ok()).toBeTruthy();
  await page.goto('/admin');
  await page.getByRole('link', { name: 'Statistics', exact: true }).click();
  await expect(page.locator('#statisticsDetails button', { hasText: 'BrowserAdmin' })).toBeVisible();
  await page.locator('#statisticsDetails button', { hasText: 'BrowserAdmin' }).click();
  await expect(page.locator('#summaryTitle')).toHaveText('BrowserAdmin');
  await expect(page.locator('#statisticsDetails')).toContainText(name);
  await expect(page.locator('#statisticsDetails')).toContainText('csharp');
  await expect(page.locator('#statisticsBreakdown')).toContainText('Code');
  for (const theme of ['dark', 'light']) {
    if (theme === 'light') await page.locator('[data-theme-toggle]').click();
    for (const width of [1440, 375]) {
      await page.setViewportSize({ width, height: 900 });
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      await page.screenshot({ path: testInfo.outputPath(`statistics-${theme}-${width}.png`), fullPage: true });
    }
  }
  await page.locator('#statisticsCutoff').fill('2000-01-01');
  page.once('dialog', dialog => dialog.dismiss());
  await page.getByRole('button', { name: 'Delete old statistics' }).click();
  await expect(page.locator('#statisticsDetails')).toContainText(name);
  await page.getByRole('button', { name: 'Back to all users' }).click();
  await expect(page.locator('#summaryTitle')).toHaveText('All users');
});

test('ordinary administrator cannot view or prune usage', async ({ playwright, isolatedHost }) => {
  const host = await isolatedHost({ AdminUsers: [{ Name: 'Regular', Password: 'regular-pass', IsSuperAdmin: false }] });
  const request = await playwright.request.newContext({ baseURL: host.baseURL });
  try {
    await request.post('/admin/login', { headers: await csrf(request), form: { name: 'Regular', password: 'regular-pass' } });
    expect((await request.get('/admin/statistics')).status()).toBe(403);
    expect((await request.get('/admin/statistics/data')).status()).toBe(403);
    expect((await request.delete('/admin/statistics/data?before=2000-01-01T00:00:00Z', { headers: await csrf(request) })).status()).toBe(403);
  } finally {
    await request.dispose();
  }
});
