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

test('room removed before first join leaves the join controls disabled', async ({
  page,
  request
}) => {
  const created = await request.post('/admin/rooms', {
    headers: await csrf(request),
    data: { name: 'Removed before join', language: 'sql' }
  });
  expect(created.ok()).toBe(true);
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

test('admin deletes a joined room without opening the admin page', async ({ page, request, browser }) => {
  const created = await request.post('/admin/rooms', {
    headers: await csrf(request),
    data: { name: 'Interview room', language: 'csharp' }
  });
  expect(created.ok()).toBe(true);
  const roomId = (await created.json()).RoomId;

  await page.goto(`/room/${roomId}`);
  await page.locator('#displayNameInput').fill('Interviewer');
  await page.locator('#joinBtn').click();
  await expect(page.locator('#joinOverlay')).toHaveClass(/hidden/);
  const deleteButton = page.getByRole('button', { name: 'Delete room' });
  await expect(deleteButton).toBeVisible();

  const guestContext = await browser.newContext({
    baseURL: new URL(page.url()).origin,
    storageState: { cookies: [], origins: [] }
  });
  try {
    const guest = await guestContext.newPage();
    await guest.goto(`/room/${roomId}`);
    await guest.locator('#displayNameInput').fill('Candidate');
    const denied = guest.waitForResponse(
      (response) =>
        response.url().endsWith(`/admin/rooms/${roomId}`) && response.status() === 401,
      { timeout: 10000 }
    );
    await guest.locator('#joinBtn').click();
    await denied;
    await expect(guest.locator('#joinOverlay')).toHaveClass(/hidden/);
    await expect(guest.locator('#deleteRoom')).toBeHidden();

    page.once('dialog', (dialog) => dialog.dismiss());
    await deleteButton.click();
    expect((await request.get(`/room/${roomId}`)).ok()).toBe(true);

    page.once('dialog', (dialog) => dialog.accept());
    await deleteButton.click();
    await expect(page).toHaveURL('/');
    const missingRoom = await request.get(`/room/${roomId}`);
    expect(missingRoom.status()).toBe(404);
    await expect(guest.locator('#joinOverlay')).not.toHaveClass(/hidden/);
  } finally {
    await guestContext.close();
  }
});
