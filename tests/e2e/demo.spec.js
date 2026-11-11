import { test, expect } from '@playwright/test';

test('public demo is editable, resettable and uses static assets only @smoke', async ({ page }) => {
  const requests = [];
  const sockets = [];
  const errors = [];
  page.on('request', request => requests.push(new URL(request.url()).pathname));
  page.on('websocket', socket => sockets.push(socket.url()));
  page.on('pageerror', error => errors.push(error.message));
  await page.goto('/demo.html');
  await expect(page.getByRole('heading', { name: 'Try a few lines of code' })).toBeVisible();
  await expect(page.locator('#demoEditor .monaco-editor')).toBeVisible();
  await expect(page.locator('[data-shape-id]')).toHaveCount(7);
  const remote = page.locator('.board-remote-cursor');
  const initialCursor = await remote.getAttribute('style');
  await expect.poll(() => remote.getAttribute('style')).not.toBe(initialCursor);
  await page.getByRole('button', { name: 'Pause demo cursors' }).click();
  await expect(page.getByRole('button', { name: 'Resume demo cursors' })).toHaveAttribute('aria-pressed', 'true');

  await page.locator('#demoEditor .view-lines').click();
  await page.keyboard.press('Control+Home');
  await page.keyboard.type('// visitor edit');
  await expect(page.locator('#demoEditor .view-lines')).toContainText('visitor edit');
  await page.getByRole('button', { name: 'Reset code', exact: true }).click();
  await expect(page.locator('#demoEditor .view-lines')).not.toContainText('visitor edit');

  await page.locator('[data-shape-id="api"]').click();
  await page.getByRole('button', { name: 'Edit label', exact: true }).click();
  const label = page.getByRole('textbox', { name: 'Element label' });
  await label.fill('Visitor service');
  await label.press('Enter');
  await expect(page.locator('[data-shape-id="api"]')).toHaveText('Visitor service');
  await page.getByRole('button', { name: 'Undo', exact: true }).click();
  await expect(page.locator('[data-shape-id="api"]')).toHaveText('Orders API');
  await page.getByRole('button', { name: 'Redo', exact: true }).click();
  await expect(page.locator('[data-shape-id="api"]')).toHaveText('Visitor service');
  await page.getByRole('button', { name: 'Reset diagram', exact: true }).click();
  await expect(page.locator('[data-shape-id="api"]')).toHaveText('Orders API');
  await expect(page.locator('[data-shape-id]')).toHaveCount(7);
  await page.screenshot({ path: 'test-results/demo-desktop.png', fullPage: true });
  expect(errors).toEqual([]);
  expect(sockets).toEqual([]);
  expect(requests.filter(path => !/\.(html|js|css|png|ico|ttf)$/.test(path))).toEqual([]);
});

test('demo fits mobile, supports light theme and keeps reduced-motion cursors still', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.goto('/demo.html');
  const remote = page.locator('.board-remote-cursor');
  await expect(remote).toHaveCount(1);
  const initial = await remote.getAttribute('style');
  await page.clock.install();
  await page.clock.fastForward(5400);
  await expect(remote).toHaveAttribute('style', initial);
  await page.locator('[data-theme-toggle]').click();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.screenshot({ path: 'test-results/demo-mobile-light.png', fullPage: true });
});
