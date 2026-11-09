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
    const rectangle = page.locator('[data-shape-type="rectangle"]');
    await rectangle.dblclick();
    await page.getByRole('textbox', { name: 'Element label' }).fill('API gateway');
    await page.getByRole('textbox', { name: 'Element label' }).press('Enter');
    await expect(bob.locator('[data-shape-type="rectangle"]')).toHaveText('API gateway');
    // Edits to different elements from both participants must survive.
    await draw(page, 'rectangle', 810, 180, 100, 60);
    await draw(bob, 'queue', 810, 330, 100, 60);
    await expect(shapes(page)).toHaveCount(10);
    await expect(shapes(bob)).toHaveCount(10);
    await page.evaluate(() => window.__debugReconnect());
    await expect(page.locator('#connectionStatus')).toHaveText('connected');
    await expect(shapes(page)).toHaveCount(10);
    await bob.reload();
    await bob.locator('#displayNameInput').fill('Bob after reload');
    await bob.locator('#joinBtn').click();
    await expect(bob.locator('#joinOverlay')).toBeHidden();
    await expect(shapes(bob)).toHaveCount(10);
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
