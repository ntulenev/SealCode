import { test, expect } from './fixtures.js';
import { csrf } from './admin-token.js';

for (const kind of ['code', 'whiteboard']) {
  test(`${kind} room exposes its creator only to administrators`, async ({ browser, page, request }) => {
    const response = await request.post('/admin/rooms', {
      headers: await csrf(request),
      data: { name: 'Creator privacy', kind, language: 'csharp' }
    });
    expect(response.ok()).toBeTruthy();
    const { RoomId: roomId } = await response.json();
    const guestContext = await browser.newContext({ storageState: { cookies: [], origins: [] } });
    try {
      const guest = await guestContext.newPage();
      for (const [participant, isAdmin] of [[page, true], [guest, false]]) {
        await participant.goto(`/room/${roomId}`);
        await expect(participant.locator('#roomCreator')).toBeHidden();
        await participant.locator('#displayNameInput').fill(isAdmin ? 'Interviewer' : 'Candidate');
        await participant.locator('#joinBtn').click();
        await expect(participant.locator('#joinOverlay')).toBeHidden();
        if (isAdmin) {
          await expect(participant.locator('#roomCreator')).toBeVisible();
          await expect(participant.locator('#createdBy')).toHaveText('BrowserAdmin');
        } else {
          await expect(participant.locator('#roomCreator')).toBeHidden();
          await expect(participant.locator('#createdBy')).toBeEmpty();
        }
        await participant.route('**/test-signalr.js', (route) => route.fulfill({
          path: 'node_modules/@microsoft/signalr/dist/browser/signalr.js',
          contentType: 'text/javascript'
        }));
        await participant.addScriptTag({ url: '/test-signalr.js' });
        const snapshots = await participant.evaluate(async (id) => {
          const probe = new signalR.HubConnectionBuilder().withUrl('/roomHub').build();
          await probe.start();
          try {
            return [await probe.invoke('JoinRoom', id, 'Probe'), await probe.invoke('SyncYjs', id)];
          } finally {
            await probe.stop();
          }
        }, roomId);
        for (const snapshot of snapshots) {
          expect(snapshot.CreatedBy ?? null).toBe(isAdmin ? 'BrowserAdmin' : null);
        }
        await participant.reload();
        await participant.locator('#displayNameInput').fill(isAdmin ? 'Interviewer' : 'Candidate');
        await participant.locator('#joinBtn').click();
        await expect(participant.locator('#joinOverlay')).toBeHidden();
        await expect(participant.locator('#roomCreator')).toBeVisible({ visible: isAdmin });
      }
    } finally {
      await guestContext.close();
      await request.delete(`/admin/rooms/${roomId}`, { headers: await csrf(request) });
    }
  });
}
