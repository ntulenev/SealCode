import { test, expect } from './fixtures.js';
import { csrf } from './admin-token.js';

test('whiteboard library previews and copies a diagram for native Ctrl+V, collaboration and undo', async ({ browser, isolatedHost }, testInfo) => {
  const host = await isolatedHost({ Snippets: { Enabled: true, Provider: 'File' } });
  const context = await browser.newContext({ baseURL: host.baseURL,
    viewport: { width: 1440, height: 1000 }, permissions: ['clipboard-read', 'clipboard-write'] });
  const page = await context.newPage();
  const guestContext = await browser.newContext({ baseURL: host.baseURL, viewport: { width: 1440, height: 1000 },
    permissions: ['clipboard-read', 'clipboard-write'] });
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
    const bob = await guestContext.newPage();
    bob.on('pageerror', error => errors.push(error.message));
    await join(bob, 'Bob');
    const snapshot = tab => tab.locator('[data-shape-id]').evaluateAll(elements =>
      elements.map(element => element.outerHTML).sort());
    const expectSameDiagram = async (peer, source = page) => {
      const expected = await snapshot(source);
      await expect.poll(() => snapshot(peer)).toEqual(expected);
    };
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
    await expect(page.locator('.snippet-dialog')).toHaveCount(0);
    await page.locator('.board-canvas').focus();
    await expect(page.locator('.board-canvas')).toBeFocused();
    await page.keyboard.press('Control+v');
    await expect(page.locator('[data-shape-id]')).toHaveCount(5);
    await expect(bob.locator('[data-shape-id]')).toHaveCount(5);
    await expectSameDiagram(bob);
    await expect(page.locator('[data-shape-type="database"]')).toHaveText('Database');
    await page.keyboard.press('Control+z');
    await expect(bob.locator('[data-shape-id]')).toHaveCount(0);
    await page.keyboard.press('Control+Shift+z');
    await expect(bob.locator('[data-shape-id]')).toHaveCount(5);
    await expectSameDiagram(bob);
    await page.keyboard.press('Control+v');
    await expect(bob.locator('[data-shape-id]')).toHaveCount(10);
    await expectSameDiagram(bob);
    const late = await guestContext.newPage();
    late.on('pageerror', error => errors.push(error.message));
    await join(late, 'Late participant');
    await expectSameDiagram(late);

    // A guest can select/copy the received diagram; selection stays local.
    await bob.bringToFront();
    await bob.locator('.board-canvas').focus();
    await bob.keyboard.press('Control+a');
    await expect(bob.locator('.board-selection')).toHaveCount(10);
    await expect(page.locator('.board-selection')).toHaveCount(5);
    await bob.keyboard.press('Control+c');
    const copiedByGuest = JSON.parse(await bob.evaluate(() => navigator.clipboard.readText()));
    expect(copiedByGuest.shapes).toHaveLength(10);
    expect(new Set(copiedByGuest.shapes.map(shape => shape.id)).size).toBe(10);
    await bob.keyboard.press('Delete');
    for (const tab of [page, bob, late]) await expect(tab.locator('[data-shape-id]')).toHaveCount(0);
    await bob.keyboard.press('Control+z');
    await expect(bob.locator('[data-shape-id]')).toHaveCount(10);
    await expectSameDiagram(page, bob);
    await expectSameDiagram(late, bob);
    await bob.keyboard.press('Control+Shift+z');
    await expect(late.locator('[data-shape-id]')).toHaveCount(0);
    await bob.keyboard.press('Control+z');
    await expect(page.locator('[data-shape-id]')).toHaveCount(10);
    // Native paste of the guest's full-board copy converges on all participants.
    await bob.keyboard.press('Control+v');
    await expect(page.locator('[data-shape-id]')).toHaveCount(20);
    await expectSameDiagram(page, bob);
    await expectSameDiagram(late, bob);
    await bob.keyboard.press('Control+z');
    await expect(page.locator('[data-shape-id]')).toHaveCount(10);
    // A peer can edit the pasted shapes, with normal Ctrl+A inside its label.
    await bob.locator('[data-shape-type="database"]').last().click();
    await bob.getByRole('button', { name: 'Edit label', exact: true }).click();
    const label = bob.getByRole('textbox', { name: 'Element label' });
    await label.press('Control+a');
    await label.press('Backspace');
    await label.fill('Edited by Bob');
    await label.press('Enter');
    await expect(page.locator('[data-shape-type="database"]').filter({ hasText: 'Edited by Bob' })).toHaveCount(1);
    await expectSameDiagram(page, bob);
    await expectSameDiagram(late, bob);
    await page.goto('/admin/snippets?language=whiteboard');
    await expect(page.getByRole('img', { name: 'Whiteboard snippet preview' })).toBeVisible();
    await page.evaluate(() => document.documentElement.setAttribute('data-theme', 'light'));
    await page.screenshot({ path: testInfo.outputPath('whiteboard-snippet-light.png') });
    await page.setViewportSize({ width: 375, height: 900 });
    await expect(page.getByRole('img', { name: 'Whiteboard snippet preview' })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    expect(errors).toEqual([]);
  } finally { await Promise.all([context.close(), guestContext.close()]); }
});


test('closing the library does not steal canvas focus when the close event arrives late', async ({ browser, isolatedHost }) => {
  const host = await isolatedHost({ ConnectionStrings: { SealCode: '' }, Snippets: { Enabled: true, Provider: 'File' } });
  const context = await browser.newContext({ baseURL: host.baseURL });
  try {
    await context.request.post('/admin/login', { headers: await csrf(context.request),
      form: { name: 'BrowserAdmin', password: 'browser-pass' } });
    const response = await context.request.post('/admin/rooms', { headers: await csrf(context.request),
      data: { name: 'Dialog focus', kind: 'whiteboard' } });
    expect(response.ok()).toBe(true);
    const { RoomId: id } = await response.json();
    const page = await context.newPage();
    await page.goto(`/room/${id}`);
    await page.locator('#displayNameInput').fill('Alice'); await page.locator('#joinBtn').click();
    await expect(page.locator('#joinOverlay')).toBeHidden();
    await page.getByRole('link', { name: 'Open question library' }).click();
    // Defer only application cleanup to reproduce the queued close-event race.
    await page.locator('.snippet-dialog').evaluate(dialog => dialog.addEventListener('close', event => {
      event.stopImmediatePropagation(); dialog.dataset.closeDeferred = 'true';
    }, { capture: true, once: true }));
    await page.getByRole('button', { name: 'Close ×' }).click();
    await expect(page.locator('.snippet-dialog')).toHaveAttribute('data-close-deferred', 'true');
    const canvas = page.locator('.board-canvas');
    await canvas.focus();
    await expect(canvas).toBeFocused();
    await page.locator('.snippet-dialog').evaluate(dialog => dialog.dispatchEvent(new Event('close')));
    await expect(page.locator('.snippet-dialog')).toHaveCount(0);
    await expect(canvas).toBeFocused();
  } finally { await context.close(); }
});
