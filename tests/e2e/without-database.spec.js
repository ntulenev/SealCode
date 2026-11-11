import { test, expect } from './fixtures.js';
import { csrf } from './admin-token.js';

test('without database configuration admins use rooms and file snippets while statistics stay disabled', async ({ browser, isolatedHost }) => {
  const host = await isolatedHost({
    ConnectionStrings: { Statistics: '', Snippets: '' },
    Snippets: { Enabled: true, Provider: 'File' }
  });
  const context = await browser.newContext({ baseURL: host.baseURL });
  const page = await context.newPage();
  try {
    await page.goto('/admin/login');
    await page.locator('input[name="name"]').fill('BrowserAdmin');
    await page.locator('input[name="password"]').fill('browser-pass');
    await page.locator('button[type="submit"]').click();
    await expect(page).toHaveURL(`${host.baseURL}/admin`);
    await expect(page.locator('#adminGreeting')).toHaveText('Hello, BrowserAdmin!');
    expect((await (await context.request.get('/admin/me')).json()).StatisticsEnabled).toBe(false);
    await expect(page.getByRole('link', { name: 'Statistics', exact: true })).toHaveCount(0);
    for (const kind of ['code', 'whiteboard']) {
      const created = await context.request.post('/admin/rooms', {
        headers: await csrf(context.request), data: { name: `No database ${kind}`, kind, language: 'csharp' }
      });
      expect(created.ok()).toBeTruthy();
      const { RoomId } = await created.json();
      await page.goto(`/room/${RoomId}`);
      await page.locator('#displayNameInput').fill('Interviewer');
      await page.locator('#joinBtn').click();
      await expect(page.locator('#joinOverlay')).toBeHidden();
      const deleted = await context.request.delete(`/admin/rooms/${RoomId}`, { headers: await csrf(context.request) });
      expect(deleted.ok()).toBeTruthy();
    }
    const snippets = await context.request.get('/admin/snippets/catalog');
    expect(snippets.ok()).toBeTruthy();
    expect((await snippets.json()).length).toBeGreaterThan(0);
    await page.goto('/admin/snippets');
    await expect(page.locator('#snippetQuestions button')).not.toHaveCount(0);
    expect((await context.request.get('/admin/statistics')).status()).toBe(404);
    expect((await context.request.get('/admin/statistics/data')).status()).toBe(404);
    expect((await context.request.delete('/admin/statistics/data?before=2000-01-01T00:00:00Z', { headers: await csrf(context.request) })).status()).toBe(404);
  } finally {
    await context.close();
  }
});
