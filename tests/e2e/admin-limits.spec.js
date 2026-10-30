import { test, expect } from './fixtures.js';

test('limits page shows server configuration and fits both themes and screen sizes', async ({ page, request }, testInfo) => {
  const config = await (await request.get('/admin/limits/config')).json();
  await page.goto('/admin');
  await page.getByRole('navigation', { name: 'Administration' }).getByRole('link', { name: 'Limits' }).click();
  await expect(page).toHaveURL(/\/admin\/limits$/);
  await expect(page.locator('#adminGreeting')).toHaveText('Hello, BrowserAdmin!');
  const capacity = page.locator('#capacityLimits dd');
  await expect(capacity).toHaveText([
    config.Limits.MaxRooms, config.MaxUsersPerRoom, config.Limits.MaxConnections, config.Limits.MaxConnectionsPerSource
  ].map((value) => new Intl.NumberFormat('en-US').format(value)));
  await expect(page.locator('#lifecycleLimits dd').first()).toHaveText('Disabled');
  await expect(page.getByRole('button', { name: 'Retry' })).toBeHidden();
  await expect(page.locator('#technicalLimits')).toBeHidden();
  await page.getByText('Technical limits', { exact: true }).click();
  await expect(page.locator('#technicalLimits dd')).toHaveCount(9);
  for (const theme of ['dark', 'light']) {
    if (theme === 'light') await page.locator('[data-theme-toggle]').click();
    for (const width of [1440, 375]) {
      await page.setViewportSize({ width, height: 900 });
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      await page.screenshot({ path: testInfo.outputPath(`limits-${theme}-${width}.png`), fullPage: true });
    }
  }
  await page.getByRole('navigation', { name: 'Administration' }).getByRole('link', { name: 'Rooms' }).click();
  await expect(page.locator('#myRoomsOnly')).toBeChecked();
});
