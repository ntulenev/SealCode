import { test, expect } from './fixtures.js';
import { csrf } from './admin-token.js';

test.use({ actionTimeout: 10000 });

async function join(page, path, name) {
  await page.goto(path);
  await page.locator('#displayNameInput').fill(name);
  await page.locator('#joinBtn').click();
  await expect(page.locator('#joinOverlay')).toBeHidden();
}

async function draw(page, tool, x, y, width = 150, height = 90) {
  await page.bringToFront();
  await page.locator(`button[data-tool="${tool}"]`).click();
  const box = await page.locator('.board-canvas').boundingBox();
  await page.mouse.move(box.x + x, box.y + y);
  await page.mouse.down();
  await page.mouse.move(box.x + x + width, box.y + y + height, { steps: 3 });
  await page.mouse.up();
}

test('whiteboard Ctrl+C / Ctrl+V uses the clipboard, synchronizes copies and preserves label editing', async ({ page, context }) => {
  const response = await page.request.post('/admin/rooms', {
    headers: await csrf(page.request), data: { name: 'Clipboard diagram', kind: 'whiteboard' }
  });
  expect(response.ok()).toBe(true);
  const { RoomId: id } = await response.json();
  const bob = await context.newPage();
  const shapes = (p) => p.locator('[data-shape-id]');
  try {
    await join(page, `/room/${id}`, 'Alice');
    await join(bob, `/room/${id}`, 'Bob');
    await draw(page, 'rectangle', 100, 180);
    await page.keyboard.press('Enter');
    const editor = page.getByRole('textbox', { name: 'Element label' });
    await editor.fill('API gateway');
    await editor.press('Enter');
    const originalId = await shapes(page).first().getAttribute('data-shape-id');
    const originalX = Number(await shapes(page).first().locator('rect').getAttribute('x'));
    await page.keyboard.press('Control+c');
    await page.keyboard.press('Control+v');
    await expect(shapes(page)).toHaveCount(2);
    await expect(shapes(bob)).toHaveCount(2);
    const firstCopyId = await shapes(page).nth(1).getAttribute('data-shape-id');
    expect(firstCopyId).not.toBe(originalId);
    await expect(shapes(page).nth(1)).toHaveText('API gateway');
    await expect(shapes(page).nth(1).locator('rect')).toHaveAttribute('x', String(originalX + 24));
    await page.keyboard.press('Control+v');
    await expect(shapes(bob)).toHaveCount(3);
    await expect(shapes(page).nth(2).locator('rect')).toHaveAttribute('x', String(originalX + 48));
    await page.keyboard.press('Control+z');
    await expect(shapes(bob)).toHaveCount(2);
    await page.keyboard.press('Control+Shift+z');
    await expect(shapes(bob)).toHaveCount(3);
    // The OS clipboard is available in another participant's tab as well.
    await bob.bringToFront();
    await bob.locator('.board-canvas').focus();
    await bob.keyboard.press('Control+v');
    await expect(shapes(page)).toHaveCount(4);
    await bob.keyboard.press('Enter');
    const label = bob.getByRole('textbox', { name: 'Element label' });
    await label.press('Control+a');
    await label.press('Control+c');
    await label.press('End');
    await label.press('Control+v');
    await expect(label).toHaveValue('API gatewayAPI gateway');
    await label.press('Enter');
    await expect(shapes(page)).toHaveCount(4);
    await expect(shapes(page).last()).toHaveText('API gatewayAPI gateway');
    await bob.keyboard.press('Delete');
    await expect(shapes(page)).toHaveCount(3);
    await expect(page.locator(`[data-shape-id="${originalId}"]`)).toHaveText('API gateway');
  } finally {
    await bob.close();
    await page.request.delete(`/admin/rooms/${id}`, { headers: await csrf(page.request) });
  }
});

test('whiteboard creation, collaborative diagrams, selective undo and reconnect @smoke', async ({ page, context }, testInfo) => {
  await page.goto('/admin');
  await page.locator('#whiteboardName').fill('Design interview');
  await page.locator('#createWhiteboard').click();
  const row = page.locator('.room-row').filter({ hasText: 'Design interview' });
  await expect(row.locator('.badge')).toHaveText('Whiteboard');
  const path = await row.locator('a').getAttribute('href');
  const bob = await context.newPage();
  try {
    await join(page, path, 'Alice');
    await join(bob, path, 'Bob');
    await expect(page.locator('#participantsList li')).toHaveCount(2);
    await expect(page.locator('#languageSelect')).toHaveCount(0);
    expect(await page.evaluate(() => !!window.monaco)).toBe(false);
    const shapes = (p) => p.locator('[data-shape-id]');
    await draw(page, 'rectangle', 70, 160);
    await expect(shapes(page)).toHaveCount(1);
    await expect(shapes(bob)).toHaveCount(1);
    const zoomIn = page.getByRole('button', { name: 'Zoom in', exact: true });
    await zoomIn.focus();
    await page.keyboard.press('Enter');
    await expect(page.getByRole('button', { name: 'Reset view', exact: true })).toHaveText('120%');
    await expect(page.getByRole('textbox', { name: 'Element label' })).toHaveCount(0);
    await page.getByRole('button', { name: 'Reset view', exact: true }).click();
    await draw(bob, 'database', 440, 160);
    await expect(shapes(page)).toHaveCount(2);
    await page.locator('.board-controls').getByRole('button', { name: 'Undo', exact: true }).click();
    await expect(shapes(bob)).toHaveCount(1);
    await expect(bob.locator('[data-shape-type="database"]')).toHaveCount(1);
    await page.locator('.board-controls').getByRole('button', { name: 'Redo', exact: true }).click();
    await expect(shapes(bob)).toHaveCount(2);
    await draw(page, 'arrow', 240, 200, 180, 0);
    await draw(page, 'queue', 250, 330, 160, 80);
    await draw(page, 'ellipse', 650, 170, 130, 80);
    await draw(page, 'diamond', 650, 330, 130, 90);
    await draw(page, 'line', 100, 310, 120, -60);
    await draw(page, 'text', 80, 410, 140, 45);
    await page.getByRole('textbox', { name: 'Element label' }).fill('Order service');
    await page.getByRole('textbox', { name: 'Element label' }).press('Enter');
    await expect(bob.locator('[data-shape-type="text"]')).toHaveText('Order service');
    await expect(shapes(bob)).toHaveCount(8);
    await draw(page, 'bidirectional-arrow', 590, 450, -150, -40);
    const doubleArrow = (p) => p.locator('[data-shape-type="bidirectional-arrow"]');
    await expect(doubleArrow(page).locator('path')).toHaveCount(2);
    await expect(doubleArrow(bob).locator('path')).toHaveCount(2);
    const arrowSvg = await doubleArrow(page).evaluate((el) => el.outerHTML);
    await expect.poll(() => doubleArrow(bob).evaluate((el) => el.outerHTML)).toBe(arrowSvg);
    await page.locator('.board-controls').getByRole('button', { name: 'Undo', exact: true }).click();
    await expect(doubleArrow(bob)).toHaveCount(0);
    await expect(shapes(bob)).toHaveCount(8);
    await page.locator('.board-controls').getByRole('button', { name: 'Redo', exact: true }).click();
    await expect(doubleArrow(bob).locator('path')).toHaveCount(2);
    await expect(shapes(bob)).toHaveCount(9);
    const rectangle = page.locator('[data-shape-type="rectangle"]');
    await rectangle.dblclick();
    await page.getByRole('textbox', { name: 'Element label' }).fill('API gateway');
    await page.getByRole('textbox', { name: 'Element label' }).press('Enter');
    await expect(bob.locator('[data-shape-type="rectangle"]')).toHaveText('API gateway');
    // Edits to different elements from both participants must survive.
    await draw(page, 'rectangle', 810, 180, 100, 60);
    await draw(bob, 'queue', 810, 330, 100, 60);
    await expect(shapes(page)).toHaveCount(11);
    await expect(shapes(bob)).toHaveCount(11);
    await page.evaluate(() => window.__debugReconnect());
    await expect(page.locator('#connectionStatus')).toHaveText('connected');
    await expect(shapes(page)).toHaveCount(11);
    await bob.reload();
    await bob.locator('#displayNameInput').fill('Bob after reload');
    await bob.locator('#joinBtn').click();
    await expect(bob.locator('#joinOverlay')).toBeHidden();
    await expect(shapes(bob)).toHaveCount(11);
    await expect.poll(() => doubleArrow(bob).evaluate((el) => el.outerHTML)).toBe(arrowSvg);
    await page.getByRole('button', { name: 'Fit diagram' }).click();
    for (const theme of ['dark', 'light']) {
      if (theme === 'light') await page.locator('.site-header [data-theme-toggle]').click();
      await page.screenshot({ path: testInfo.outputPath(`whiteboard-${theme}.png`), fullPage: true });
    }
    await page.setViewportSize({ width: 375, height: 900 });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: testInfo.outputPath('whiteboard-mobile.png'), fullPage: true });
  } finally {
    await bob.close();
    await page.request.delete(`/admin/rooms/${path.split('/').pop()}`, { headers: await csrf(page.request) });
  }
});

test('whiteboard checkpoints restore the whole diagram for a late participant', async ({ page, context, isolatedHost }) => {
  const host = await isolatedHost({ Limits: { CheckpointEveryRevisions: 1 } });
  await page.goto(`${host.baseURL}/admin/login`);
  await page.locator('input[name="name"]').fill('BrowserAdmin');
  await page.locator('input[name="password"]').fill('browser-pass');
  await page.locator('button[type="submit"]').click();
  await expect(page.locator('#adminGreeting')).toHaveText('Hello, BrowserAdmin!');
  await page.locator('#whiteboardName').fill('Checkpoint diagram');
  await page.locator('#createWhiteboard').click();
  const row = page.locator('.room-row').filter({ hasText: 'Checkpoint diagram' });
  await expect(row.locator('.badge')).toHaveText('Whiteboard');
  const path = await row.locator('a').getAttribute('href');
  const pendingCheckpoints = new Set();
  let acceptedCheckpoint = false;
  page.on('websocket', (socket) => {
    socket.on('framesent', ({ payload }) => {
      for (const frame of String(payload).split('\x1e').filter(Boolean)) {
        const message = JSON.parse(frame);
        if (message.target === 'CheckpointYjs') pendingCheckpoints.add(message.invocationId);
      }
    });
    socket.on('framereceived', ({ payload }) => {
      for (const frame of String(payload).split('\x1e').filter(Boolean)) {
        const message = JSON.parse(frame);
        if (pendingCheckpoints.has(message.invocationId) && message.result === true) acceptedCheckpoint = true;
      }
    });
  });
  await join(page, `${host.baseURL}${path}`, 'Alice');
  await expect(page).toHaveTitle('Whiteboard — SealCode');
  await draw(page, 'database', 100, 160);
  await expect.poll(() => acceptedCheckpoint).toBe(true);
  const late = await context.newPage();
  try {
    await join(late, `${host.baseURL}${path}`, 'Late participant');
    await expect(late.locator('[data-shape-type="database"]')).toHaveCount(1);
    await draw(late, 'queue', 400, 180);
    await expect(page.locator('[data-shape-id]')).toHaveCount(2);
  } finally { await late.close(); }
});

test('mixed room capacity, participant capacity and deletion use the existing limits', async ({ page, context, isolatedHost }) => {
  const host = await isolatedHost({ MaxUsersPerRoom: 1, Limits: { MaxRooms: 2 } });
  await page.goto(`${host.baseURL}/admin/login`);
  await page.locator('input[name="name"]').fill('BrowserAdmin');
  await page.locator('input[name="password"]').fill('browser-pass');
  await page.locator('button[type="submit"]').click();
  await expect(page.locator('#adminGreeting')).toHaveText('Hello, BrowserAdmin!');
  const token = { 'X-CSRF-TOKEN': (await (await page.request.get(`${host.baseURL}/admin/token`)).json()).Token };
  const code = await page.request.post(`${host.baseURL}/admin/rooms`, { headers: token, data: { name: 'Code', language: 'sql' } });
  expect(code.ok()).toBe(true);
  const boardResponse = await page.request.post(`${host.baseURL}/admin/rooms`, { headers: token, data: { name: 'Board', kind: 'whiteboard' } });
  expect(boardResponse.ok()).toBe(true);
  const board = await boardResponse.json();
  const excess = await page.request.post(`${host.baseURL}/admin/rooms`, { headers: token, data: { name: 'Overflow', kind: 'whiteboard' } });
  expect(excess.status()).toBe(409);
  await join(page, `${host.baseURL}/room/${board.RoomId}`, 'Alice');
  const guest = await context.newPage();
  try {
    await guest.goto(`${host.baseURL}/room/${board.RoomId}`);
    await guest.locator('#displayNameInput').fill('Bob');
    await guest.locator('#joinBtn').click();
    await expect(guest.locator('#joinError')).toContainText('full');
    await draw(page, 'rectangle', 100, 160);
    const response = await page.request.delete(`${host.baseURL}/admin/rooms/${board.RoomId}`, { headers: token });
    expect(response.ok()).toBe(true);
    await expect(page.locator('#joinOverlay')).toBeVisible();
    await expect(page.locator('#joinError')).toContainText('deleted');
    await expect(page.locator('button[data-tool="rectangle"]')).toBeDisabled();
  } finally { await guest.close(); }
});
