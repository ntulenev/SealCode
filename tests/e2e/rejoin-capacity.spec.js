import { test, expect } from '@playwright/test';
import { csrf } from './admin-token.js';
import { startIsolatedHost } from '../isolated-host.js';

async function join(page, roomId, name) {
  await page.goto(`/room/${roomId}`);
  await page.locator('#displayNameInput').fill(name);
  await page.locator('#joinBtn').click();
  await expect(page.locator('#joinOverlay')).toHaveClass(/hidden/);
}

test('a full room makes a failed rejoin visible and keeps the editor read-only', async ({
  browser
}) => {
  const host = await startIsolatedHost({ MaxUsersPerRoom: 2 });
  const aliceContext = await browser.newContext({ baseURL: host.baseURL });
  const others = await browser.newContext({ baseURL: host.baseURL });
  try {
    const alice = await aliceContext.newPage();
    await alice.request.post('/admin/login', {
      headers: await csrf(alice.request),
      form: { name: 'BrowserAdmin', password: 'browser-pass' }
    });
    const response = await alice.request.post('/admin/rooms', {
      headers: await csrf(alice.request),
      data: { name: 'Capacity', language: 'csharp' }
    });
    const roomId = (await response.json()).RoomId;
    const bob = await others.newPage();
    await join(alice, roomId, 'Alice');
    await join(bob, roomId, 'Bob');
    await aliceContext.setOffline(true);
    await alice.evaluate(() =>
      monaco.editor
        .getModels()[0]
        .applyEdits([
          { range: new monaco.Range(1, 1, 1, 1), text: 'unsent offline edit' }
        ])
    );
    await expect(bob.locator('#participantsList li')).toHaveCount(1, { timeout: 30000 });
    const carol = await others.newPage();
    await join(carol, roomId, 'Carol');
    await aliceContext.setOffline(false);
    await expect(alice.locator('#syncError')).toContainText('Room is full', {
      timeout: 30000
    });
    expect(
      await alice.evaluate(() =>
        monaco.editor.getEditors()[0].getOption(monaco.editor.EditorOption.readOnly)
      )
    ).toBe(true);
    await expect(alice.locator('#syncError')).not.toContainText(
      'Changes could not be sent'
    );
  } finally {
    await aliceContext.close();
    await others.close();
    await host.stop();
  }
});
