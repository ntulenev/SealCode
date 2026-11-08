import { test, expect } from './fixtures.js';
import { csrf } from './admin-token.js';

test('private library works in popup, new tab and room with highlighted copyable questions', async ({ browser, isolatedHost }, testInfo) => {
  const host = await isolatedHost({ Snippets: { Enabled: true, SuperAdminsOnly: true } });
  const context = await browser.newContext({ baseURL: host.baseURL, viewport: { width: 1440, height: 1000 }, permissions: ['clipboard-read', 'clipboard-write'] });
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  try {
    await context.request.post('/admin/login', { headers: await csrf(context.request), form: { name: 'BrowserAdmin', password: 'browser-pass' } });
    const catalogResponse = await context.request.get('/admin/snippets/catalog');
    const syncedAt = catalogResponse.headers()['x-snippets-synced-at'];
    expect(Number.isFinite(Date.parse(syncedAt))).toBe(true);
    const syncIso = new Date(syncedAt).toISOString();
    await page.goto('/admin');
    await page.getByRole('link', { name: 'Open question library' }).click();
    const dialog = page.getByRole('dialog', { name: 'Question library' });
    await expect(dialog).toBeVisible();
    const library = page.frameLocator('iframe[title="Question library"]');
    await expect(library.locator('#snippetQuestions button')).toHaveCount(3);
    await expect(library.locator('#snippetSyncTime')).toHaveAttribute('datetime', syncIso);
    await expect(library.locator('#snippetSyncTime')).toBeVisible();
    await expect(library.locator('#snippetSyncTime')).not.toHaveText(/Loading|Unavailable/);
    await expect(library.locator('.site-header a')).toHaveCount(0);
    await library.getByRole('button', { name: 'Copy snippet' }).click();
    await expect(library.getByRole('status')).toHaveText('Copied to clipboard.');
    expect(await page.evaluate(() => navigator.clipboard.readText())).toContain('network partition');
    await library.getByRole('button', { name: 'C#', exact: true }).click();
    await expect(library.locator('#snippetDescription')).toContainText('bounded concurrency');
    await expect(library.locator('.snippet-question-description').first()).toContainText('cancellation propagation');
    const search = library.getByRole('searchbox', { name: 'Search questions' });
    await search.fill(' BOUNDED concurrency ');
    await expect(library.locator('#snippetQuestions button')).toHaveCount(1);
    await expect(library.locator('#snippetSearchSummary')).toHaveText('1 of 3 questions in C#');
    await search.fill('03');
    await expect(library.locator('#snippetTitle')).toHaveText('03 / Equality and dictionary keys');
    await search.fill('no such question');
    await expect(library.locator('#snippetQuestions button')).toHaveCount(0);
    await expect(library.getByRole('button', { name: 'Copy snippet' })).toBeDisabled();
    await expect(library.locator('#snippetTitle')).toHaveText('No matching questions');
    await search.fill('NULL');
    await library.getByRole('button', { name: 'SQL', exact: true }).click();
    await expect(library.locator('#snippetQuestions button')).toHaveCount(1);
    await expect(library.locator('#snippetTitle')).toHaveText('02 / Outer joins and NULL');
    await search.fill('');
    await library.getByRole('button', { name: 'C#', exact: true }).click();
    await expect(library.locator('#snippetQuestions button')).toHaveCount(3);
    await expect(library.locator('.snippet-code .mtk1, .snippet-code [class^="mtk"]')).not.toHaveCount(0);
    await library.getByRole('button', { name: 'Copy snippet' }).click();
    await expect(library.getByRole('status')).toHaveText('Copied to clipboard.');
    expect(await page.evaluate(() => navigator.clipboard.readText())).toContain('CountSuccessfulAsync');
    await page.screenshot({ path: testInfo.outputPath('snippets-popup.png'), fullPage: true });
    if (process.env.CAPTURE_SNIPPETS) await page.screenshot({ path: 'docs/images/snippets-popup.png', fullPage: true });
    const [tab] = await Promise.all([context.waitForEvent('page'), dialog.getByRole('link', { name: 'Open in new tab' }).click()]);
    await expect(tab).toHaveURL(`${host.baseURL}/admin/snippets`);
    await expect(tab.locator('#snippetSyncTime')).toHaveAttribute('datetime', syncIso);
    await expect(tab.getByRole('link', { name: 'Back to rooms' })).toBeVisible();
    await tab.getByRole('button', { name: 'SQL', exact: true }).click();
    await expect(tab.locator('.snippet-code [class^="mtk"]')).not.toHaveCount(0);
    await tab.screenshot({ path: testInfo.outputPath('snippets-tab.png'), fullPage: true });
    if (process.env.CAPTURE_SNIPPETS) await tab.screenshot({ path: 'docs/images/snippets-tab.png', fullPage: true });
    for (const width of [375, 1440]) {
      await tab.setViewportSize({ width, height: 900 });
      expect(await tab.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    }
    await tab.close();
    await dialog.getByRole('button', { name: 'Close' }).click();
    await expect(dialog).toHaveCount(0);
    await expect(page.getByRole('link', { name: 'Open question library' })).toBeFocused();
    const response = await context.request.post('/admin/rooms', { headers: await csrf(context.request), data: { Name: 'Snippet interview', Language: 'csharp' } });
    const room = await response.json();
    await page.goto(`/room/${room.RoomId}`);
    await page.locator('#joinBtn').click();
    await expect(page.locator('#joinOverlay')).toBeHidden();
    await page.evaluate(() => window.monaco.editor.getModels()[0].setValue('Keep the shared document unchanged.'));
    await page.getByRole('link', { name: 'Open question library' }).click();
    await expect(page.getByRole('dialog', { name: 'Question library' })).toBeVisible();
    await expect(library.locator('#snippetTabs [data-language="csharp"]')).toHaveAttribute('aria-pressed', 'true');
    await library.getByRole('button', { name: 'Copy snippet' }).click();
    await expect(library.getByRole('status')).toHaveText('Copied to clipboard.');
    expect(await page.evaluate(() => window.monaco.editor.getModels()[0].getValue())).toBe('Keep the shared document unchanged.');
    expect(errors).toEqual([]);
  } finally {
    await context.close();
  }
});

test('library opens the current room language and falls back to General in popups and tabs', async ({ browser, isolatedHost }) => {
  const host = await isolatedHost({ Snippets: { Enabled: true, SuperAdminsOnly: true } });
  const context = await browser.newContext({ baseURL: host.baseURL });
  try {
    await context.request.post('/admin/login', { headers: await csrf(context.request), form: { name: 'BrowserAdmin', password: 'browser-pass' } });
    const response = await context.request.post('/admin/rooms', { headers: await csrf(context.request), data: { Name: 'Language-aware library', Language: 'sql' } });
    expect(response.ok()).toBe(true);
    const room = await response.json();
    const page = await context.newPage();
    await page.goto(`/room/${room.RoomId}`);
    await page.locator('#joinBtn').click();
    await expect(page.locator('#joinOverlay')).toBeHidden();
    const book = page.getByRole('link', { name: 'Open question library' });
    const dialog = page.getByRole('dialog', { name: 'Question library' });
    const library = page.frameLocator('iframe[title="Question library"]');
    for (const [language, expected] of [['sql', 'sql'], ['javascript', 'general'], ['csharp', 'csharp']]) {
      await page.locator('#languageSelect').selectOption(language);
      await expect.poll(() => page.evaluate(() => window.monaco.editor.getModels()[0].getLanguageId())).toBe(language);
      await book.click();
      await expect(library.locator(`#snippetTabs [data-language="${expected}"]`)).toHaveAttribute('aria-pressed', 'true');
      const [tab] = await Promise.all([context.waitForEvent('page'), dialog.getByRole('link', { name: 'Open in new tab' }).click()]);
      await expect(tab).toHaveURL(`${host.baseURL}/admin/snippets?language=${language}`);
      await expect(tab.locator(`#snippetTabs [data-language="${expected}"]`)).toHaveAttribute('aria-pressed', 'true');
      await tab.close();
      await dialog.getByRole('button', { name: 'Close' }).click();
    }
    const [tab] = await Promise.all([context.waitForEvent('page'), book.click({ modifiers: ['ControlOrMeta'] })]);
    await expect(tab).toHaveURL(`${host.baseURL}/admin/snippets?language=csharp`);
    await expect(tab.locator('#snippetTabs [data-language="csharp"]')).toHaveAttribute('aria-pressed', 'true');
    await tab.close();
  } finally { await context.close(); }
});

test('disabled library and guest rooms hide the book control', async ({ page, browser, isolatedHost }) => {
  await page.goto('/admin');
  await expect.poll(async () => (await (await page.request.get('/admin/snippets/access')).json()).CanAccess).toBe(false);
  await expect(page.getByRole('link', { name: 'Open question library' })).toHaveCount(0);
  const host = await isolatedHost({ Snippets: { Enabled: true, SuperAdminsOnly: true } });
  const guest = await browser.newContext({ baseURL: host.baseURL });
  const owner = await browser.newContext({ baseURL: host.baseURL });
  try {
    await owner.request.post('/admin/login', { headers: await csrf(owner.request), form: { name: 'BrowserAdmin', password: 'browser-pass' } });
    const response = await owner.request.post('/admin/rooms', { headers: await csrf(owner.request), data: { Name: 'Guest room', Language: 'sql' } });
    expect(response.ok()).toBe(true);
    const room = await response.json();
    expect((await guest.request.get('/admin/snippets/catalog')).status()).toBe(401);
    for (const path of ['/Resources/Snippets/catalog.json', '/Resources/Snippets/general-01.md', '/Resources/Snippets/library.html'])
      expect((await guest.request.get(path)).status()).toBe(404);
    const guestPage = await guest.newPage();
    await guestPage.goto(`/room/${room.RoomId}`);
    await guestPage.locator('#displayNameInput').fill('Guest');
    await guestPage.locator('#joinBtn').click();
    await expect(guestPage.locator('#joinOverlay')).toBeHidden();
    expect((await (await guest.request.get('/admin/snippets/access')).json()).CanAccess).toBe(false);
    await expect(guestPage.getByRole('link', { name: 'Open question library' })).toHaveCount(0);
  } finally {
    await guest.close();
    await owner.close();
  }
});

for (const superAdminsOnly of [true, false]) {
  test(`ordinary admin library access follows SuperAdminsOnly=${superAdminsOnly}`, async ({ browser, isolatedHost }) => {
    const host = await isolatedHost({
      Snippets: { Enabled: true, SuperAdminsOnly: superAdminsOnly },
      AdminUsers: [{ Name: 'RegularAdmin', Password: 'regular-pass', IsSuperAdmin: false }]
    });
    const context = await browser.newContext({ baseURL: host.baseURL });
    try {
      const login = await context.request.post('/admin/login', {
        headers: await csrf(context.request), form: { name: 'RegularAdmin', password: 'regular-pass' }
      });
      expect(login.ok()).toBe(true);
      const page = await context.newPage();
      await page.goto('/admin');
      const allowed = !superAdminsOnly;
      expect((await (await context.request.get('/admin/snippets/access')).json()).CanAccess).toBe(allowed);
      expect((await context.request.get('/admin/snippets/catalog')).status()).toBe(allowed ? 200 : 403);
      const book = page.getByRole('link', { name: 'Open question library' });
      if (allowed) {
        await book.click();
        await expect(page.frameLocator('iframe[title="Question library"]').locator('#snippetQuestions button')).toHaveCount(3);
        await page.getByRole('button', { name: 'Close' }).click();
      } else {
        await expect(book).toHaveCount(0);
        const denied = await page.goto('/admin/snippets');
        expect(denied.status()).toBe(403);
        await expect(page.locator('#snippetContent')).toHaveCount(0);
      }
      await context.request.post('/admin/logout', { headers: await csrf(context.request) });
      expect((await context.request.get('/admin/snippets/catalog')).status()).toBe(401);
      await page.goto('/admin/snippets');
      await expect(page).toHaveURL(`${host.baseURL}/admin/login`);
    } finally { await context.close(); }
  });
}
