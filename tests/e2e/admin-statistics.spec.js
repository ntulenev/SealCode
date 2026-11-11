import { test, expect } from './fixtures.js';
import { csrf } from './admin-token.js';

test('super admin can inspect usage and confirm or cancel retention', async ({ page, request }, testInfo) => {
  const name = 'C# interview sample';
  const created = await request.post('/admin/rooms', { headers: await csrf(request), data: { name, language: 'csharp' } });
  expect(created.ok()).toBeTruthy();
  const whiteboard = await request.post('/admin/rooms', { headers: await csrf(request), data: { name: 'Service design sample', kind: 'whiteboard' } });
  expect(whiteboard.ok()).toBeTruthy();
  await page.goto('/admin');
  await page.getByRole('link', { name: 'Statistics', exact: true }).click();
  await expect(page.locator('#statisticsDetails button', { hasText: 'BrowserAdmin' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Back to all users' })).toBeHidden();
  await page.screenshot({ path: testInfo.outputPath('statistics-overview-dark.png'), fullPage: true });
  await page.locator('[data-theme-toggle]').click();
  await page.screenshot({ path: testInfo.outputPath('statistics-overview-light.png'), fullPage: true });
  await page.locator('[data-theme-toggle]').click();
  await page.locator('#statisticsDetails button', { hasText: 'BrowserAdmin' }).click();
  await expect(page.locator('#summaryTitle')).toHaveText('BrowserAdmin');
  await expect(page.locator('#statisticsDetails')).toContainText(name);
  await expect(page.locator('#statisticsDetails')).toContainText('csharp');
  await expect(page.locator('#statisticsBreakdown')).toContainText('Code');
  await expect(page.locator('#statisticsBreakdown')).toContainText('Whiteboard');
  await expect(page.locator('#statisticsDetails')).toContainText('Service design sample');
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
  page.once('dialog', dialog => dialog.accept());
  const deletion = page.waitForResponse(response => response.request().method() === 'DELETE' && response.url().includes('/admin/statistics/data'));
  await page.getByRole('button', { name: 'Delete old statistics' }).click();
  const deleted = await deletion;
  expect(deleted.ok()).toBeTruthy();
  expect((await deleted.json()).DeletedEvents).toBe(0);
  await expect(page.locator('#statisticsStatus')).toHaveText('0 old events deleted.');
  await expect(page.locator('#statisticsDetails')).toContainText(name);
  await page.getByRole('button', { name: 'Back to all users' }).click();
  await expect(page.locator('#summaryTitle')).toHaveText('All users');
});

test('ordinary administrator cannot view or prune usage', async ({ playwright, isolatedHost }) => {
  const host = await isolatedHost({ AdminUsers: [{ Name: 'Regular', Password: 'regular-pass', IsSuperAdmin: false }] });
  const request = await playwright.request.newContext({ baseURL: host.baseURL });
  try {
    expect((await request.get('/admin/statistics')).status()).toBe(401);
    expect((await request.get('/admin/statistics/data')).status()).toBe(401);
    await request.post('/admin/login', { headers: await csrf(request), form: { name: 'Regular', password: 'regular-pass' } });
    expect((await request.get('/admin/statistics')).status()).toBe(403);
    expect((await request.get('/admin/statistics/data')).status()).toBe(403);
    expect((await request.delete('/admin/statistics/data?before=2000-01-01T00:00:00Z', { headers: await csrf(request) })).status()).toBe(403);
  } finally {
    await request.dispose();
  }
});


test('history pagination and room names remain safe in the browser', async ({ page, request }) => {
  const maliciousName = '<img src=x onerror="window.statisticsXss=true">';
  const headers = await csrf(request);
  for (let index = 0; index < 51; index++) {
    const created = await request.post('/admin/rooms', { headers, data: { name: index === 50 ? maliciousName : `History sample ${index}`, language: 'sql' } });
    expect(created.ok()).toBeTruthy();
  }
  await page.goto('/admin/statistics');
  await page.locator('#statisticsDetails button', { hasText: 'BrowserAdmin' }).click();
  await expect(page.locator('#statisticsDetails')).toContainText(maliciousName);
  await expect(page.locator('#statisticsDetails img')).toHaveCount(0);
  expect(await page.evaluate(() => window.statisticsXss)).toBeUndefined();
  await expect(page.locator('#statisticsDetails tr')).toHaveCount(50);
  await page.getByRole('button', { name: 'Next', exact: true }).click();
  await expect(page.locator('#statisticsPage')).toContainText('51');
  await expect(page.locator('#previousPage')).toBeEnabled();
  await page.getByRole('button', { name: 'Previous', exact: true }).click();
  await expect(page.locator('#statisticsDetails')).toContainText(maliciousName);
  await expect(page.locator('#previousPage')).toBeDisabled();
});

test('period reports, server filters, table sorting and refresh spacing work together', async ({ page, request }) => {
  const prefix = `Filter sample ${Date.now()}`;
  for (const [name, kind] of [[`${prefix} Alpha`, 'code'], [`${prefix} Zeta`, 'whiteboard']]) {
    const response = await request.post('/admin/rooms', { headers: await csrf(request), data: { name, kind, language: 'csharp' } });
    expect(response.ok()).toBeTruthy();
  }
  await page.goto('/admin/statistics');
  await expect(page.locator('#statisticsSummary dd')).toHaveCount(5);
  const refresh = await page.locator('#refreshStatistics').boundingBox();
  const summary = await page.locator('section[aria-labelledby="summaryTitle"]').boundingBox();
  expect(summary.y - (refresh.y + refresh.height)).toBeGreaterThanOrEqual(20);
  await page.getByRole('tab', { name: 'Today', exact: true }).click();
  await expect(page.getByRole('tab', { name: 'Today', exact: true })).toHaveAttribute('aria-selected', 'true');
  await expect(page.locator('#statisticsReportText')).toContainText('Today:');
  await page.getByRole('tab', { name: 'Last 7 days', exact: true }).click();
  await expect(page.locator('#statisticsReportText')).toContainText('Last 7 days:');
  await page.locator('#statisticsSearch').fill(prefix);
  await page.getByRole('button', { name: 'Apply filters', exact: true }).click();
  await expect(page.locator('#statisticsSummary dd').nth(3)).not.toHaveText('—');
  await page.locator('#statisticsDetails button', { hasText: 'BrowserAdmin' }).click();
  await expect(page.locator('#statisticsDetails tr')).toHaveCount(2);
  const names = page.locator('#statisticsDetails tr td:nth-child(4)');
  await page.locator('#statisticsHead').getByRole('button', { name: /^Room/ }).click();
  await expect(names).toHaveText([`${prefix} Alpha`, `${prefix} Zeta`]);
  await page.locator('#statisticsHead').getByRole('button', { name: /^Room/ }).click();
  await expect(names).toHaveText([`${prefix} Zeta`, `${prefix} Alpha`]);
  await page.locator('#statisticsKind').selectOption('whiteboard');
  await page.getByRole('button', { name: 'Apply filters', exact: true }).click();
  await expect(names).toHaveText([`${prefix} Zeta`]);
  await page.getByRole('tab', { name: 'Custom period', exact: true }).click();
  await page.locator('#statisticsFrom').fill('2020-01-01');
  await page.locator('#statisticsTo').fill('2020-02-01');
  await page.getByRole('button', { name: 'Apply period', exact: true }).click();
  await expect(page.locator('#statisticsStatus')).toContainText('at most 31 days');
});
