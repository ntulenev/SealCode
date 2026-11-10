import { test, expect } from './fixtures.js';
import { csrf } from './admin-token.js';

test('whiteboard library previews and copies a diagram for native Ctrl+V, collaboration and undo', async ({ browser, isolatedHost }, testInfo) => {
  const host = await isolatedHost({ Snippets: { Enabled: true, Provider: 'File' } });
  const context = await browser.newContext({ baseURL: host.baseURL,
    viewport: { width: 1440, height: 1000 }, permissions: ['clipboard-read', 'clipboard-write'] });
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  try {
    await context.request.post('/admin/login', { headers: await csrf(context.request),
      form: { name: 'BrowserAdmin', password: 'browser-pass' } });
    const response = await context.request.post('/admin/rooms', { headers: await csrf(context.request),
      data: { name: 'Snippet diagram', kind: 'whiteboard' } });
    expect(response.ok()).toBe(true);
    const { RoomId: id } = await response.json();
    const join = async (tab, name) => {
      await tab.goto(`/room/${id}`);
      await tab.locator('#displayNameInput').fill(name);
      await tab.locator('#joinBtn').click();
      await expect(tab.locator('#joinOverlay')).toBeHidden();
    };
    await join(page, 'Alice');
    const bob = await context.newPage();
    await join(bob, 'Bob');
    await page.bringToFront();
    await page.getByRole('link', { name: 'Open question library' }).click();
    const library = page.frameLocator('iframe[title="Question library"]');
    await expect(library.getByRole('button', { name: 'Whiteboard', exact: true })).toHaveAttribute('aria-pressed', 'true');
    const preview = library.getByRole('img', { name: 'Whiteboard snippet preview' });
    await expect(preview).toBeVisible();
    await expect(preview.locator('[data-shape-id]')).toHaveCount(5);
    await page.screenshot({ path: testInfo.outputPath('whiteboard-snippet-preview.png') });
    await library.locator('#copySnippet').click();
    await expect(library.getByRole('status')).toHaveText('Copied to clipboard.');
    const payload = JSON.parse(await page.evaluate(() => navigator.clipboard.readText()));
    expect(payload.format).toBe('sealcode-whiteboard');
    expect(payload.shapes).toHaveLength(5);
    await page.getByRole('button', { name: 'Close ×' }).click();
    await page.locator('.board-canvas').focus();
    await page.keyboard.press('Control+v');
    await expect(page.locator('[data-shape-id]')).toHaveCount(5);
    await expect(bob.locator('[data-shape-id]')).toHaveCount(5);
    await expect(page.locator('[data-shape-type="database"]')).toHaveText('Database');
    await page.keyboard.press('Control+z');
    await expect(bob.locator('[data-shape-id]')).toHaveCount(0);
    await page.keyboard.press('Control+Shift+z');
    await expect(bob.locator('[data-shape-id]')).toHaveCount(5);
    await page.keyboard.press('Control+v');
    await expect(bob.locator('[data-shape-id]')).toHaveCount(10);
    await page.goto('/admin/snippets?language=whiteboard');
    await expect(page.getByRole('img', { name: 'Whiteboard snippet preview' })).toBeVisible();
    await page.evaluate(() => document.documentElement.setAttribute('data-theme', 'light'));
    await page.screenshot({ path: testInfo.outputPath('whiteboard-snippet-light.png') });
    await page.setViewportSize({ width: 375, height: 900 });
    await expect(page.getByRole('img', { name: 'Whiteboard snippet preview' })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    expect(errors).toEqual([]);
  } finally { await context.close(); }
});
