import { test, expect } from './fixtures.js';
import { csrf } from './admin-token.js';

test('join waits for languages and ignores a second click', async ({ page, request }) => {
  const response = await request.post('/admin/rooms', {
    headers: await csrf(request),
    data: { name: 'Language race', language: 'sql' }
  });
  const roomId = (await response.json()).RoomId;
  let release;
  const gate = new Promise((done) => {
    release = done;
  });
  await page.route('**/languages', async (route) => {
    await gate;
    await route.continue();
  });
  let joins = 0;
  page.on('websocket', (socket) =>
    socket.on('framesent', ({ payload }) => {
      if (typeof payload === 'string' && payload.includes('"target":"JoinRoom"')) joins++;
    })
  );
  await page.goto(`/room/${roomId}`);
  await page.locator('#displayNameInput').fill('Alice');
  await page.evaluate(() => {
    const button = document.getElementById('joinBtn');
    button.click();
    button.click();
  });
  await expect(page.locator('#joinBtn')).toBeDisabled();
  await expect(page.locator('#displayNameInput')).toBeDisabled();
  release();
  await expect(page.locator('#joinOverlay')).toHaveClass(/hidden/);
  await expect(page.locator('#languageSelect')).toHaveValue('sql');
  await expect(page.locator('#languageSelect')).toBeEnabled();
  expect(await page.evaluate(() => monaco.editor.getModels()[0].getLanguageId())).toBe(
    'sql'
  );
  expect(joins).toBe(1);
});

test('room removed before first join leaves the join controls disabled', async ({ page, request }) => {
  const created = await request.post('/admin/rooms', {
    headers: await csrf(request),
    data: { name: 'Removed before join', language: 'sql' }
  });
  const roomId = (await created.json()).RoomId;
  await page.goto(`/room/${roomId}`);
  const removed = await request.delete(`/admin/rooms/${roomId}`, {
    headers: await csrf(request)
  });
  expect(removed.ok()).toBeTruthy();
  await page.locator('#displayNameInput').fill('Alice');
  await page.locator('#joinBtn').click();
  await expect(page.locator('#joinError')).toContainText('Room not found');
  await expect(page.locator('#joinBtn')).toBeDisabled();
  await expect(page.locator('#displayNameInput')).toBeDisabled();
});
