import { csrf } from './admin-token.js';
import { expect, test } from '@playwright/test';

async function createRoom(page) {
  const login = await page.request.post('/admin/login', {
    headers: await csrf(page.request),
    form: { name: 'BrowserAdmin', password: 'browser-pass' }
  });
  expect(login.ok()).toBeTruthy();
  const response = await page.request.post('/admin/rooms', {
    headers: await csrf(page.request), data: { name: 'Presence', language: 'csharp' }
  });
  expect(response.ok()).toBeTruthy();
  return (await response.json()).RoomId;
}

async function join(page, roomId, name) {
  await page.goto(`/room/${roomId}`);
  await page.locator('#displayNameInput').fill(name);
  await page.locator('#joinBtn').click();
  await expect(page.locator('#joinOverlay')).toHaveClass(/hidden/);
}

for (const incomingLanguage of [undefined, 'sql']) {
  test(`failed language change restores ${incomingLanguage ?? 'the room language'}`, async ({ page }) => {
    const roomId = await createRoom(page);
    await page.routeWebSocket('**/roomHub*', route => {
      const server = route.connectToServer();
      route.onMessage(message => {
        if (typeof message !== 'string') return server.send(message);
        for (const record of message.split('\x1e').filter(Boolean)) {
          const frame = JSON.parse(record);
          if (frame.target === 'SetLanguage') {
            if (incomingLanguage) route.send(JSON.stringify({ type: 1, target: 'LanguageUpdated', arguments: [incomingLanguage, 2, 1] }) + '\x1e');
            route.send(JSON.stringify({ type: 3, invocationId: frame.invocationId, error: 'RateLimited: test refusal' }) + '\x1e');
          } else server.send(record + '\x1e');
        }
      });
    });
    await join(page, roomId, 'Alice');
    await page.locator('#languageSelect').selectOption('javascript');
    await expect(page.locator('#languageSelect')).toHaveValue(incomingLanguage ?? 'csharp');
    await expect.poll(() => page.evaluate(() => monaco.editor.getModels()[0].getLanguageId()))
      .toBe(incomingLanguage ?? 'csharp');
  });
}
