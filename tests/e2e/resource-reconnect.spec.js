import { csrf } from './admin-token.js';
import { expect, test } from './fixtures.js';

test('a refused initial connection joins after its source slot is released', async ({ browser, isolatedHost }) => {
  const host = await isolatedHost({ Limits: { MaxConnections: 1, MaxConnectionsPerSource: 1 } });
  const context = await browser.newContext({ baseURL: host.baseURL });
  try {
    const holder = await context.newPage();
    const login = await holder.request.post('/admin/login', {
      headers: await csrf(holder.request),
      form: { name: 'BrowserAdmin', password: 'browser-pass' }
    });
    expect(login.ok()).toBeTruthy();
    const created = await holder.request.post('/admin/rooms', {
      headers: await csrf(holder.request),
      data: { name: 'Reconnect', language: 'javascript' }
    });
    expect(created.ok()).toBeTruthy();
    const roomId = (await created.json()).RoomId;
    await holder.goto(`/room/${roomId}`);
    await holder.locator('#displayNameInput').fill('Holder');
    await holder.locator('#joinBtn').click();
    await expect(holder.locator('#joinOverlay')).toHaveClass(/hidden/);

    const waiting = await context.newPage();
    await waiting.goto(`/room/${roomId}`);
    await waiting.locator('#displayNameInput').fill('Waiting');
    await waiting.locator('#joinBtn').click();
    await expect(waiting.locator('#joinOverlay')).toBeVisible();
    await holder.close();
    await expect(waiting.locator('#joinOverlay')).toHaveClass(/hidden/, { timeout: 30000 });
    await expect(waiting.locator('#connectionStatus')).toHaveText('connected');
  } finally {
    await context.close();
  }
});
