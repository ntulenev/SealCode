import { test, expect } from './fixtures.js';
import { csrf } from './admin-token.js';

const theme = (page) => page.locator('html');
const toggle = (page) => page.locator('.site-header [data-theme-toggle]');

test('dark is the default even with a light OS preference; choice persists across pages', async ({ page }) => {
  await page.emulateMedia({ colorScheme: 'light' });
  await page.goto('/');
  await expect(theme(page)).toHaveAttribute('data-theme', 'dark');
  expect(await page.evaluate(() => localStorage.getItem('sealcode.theme'))).toBeNull();
  await toggle(page).click();
  await expect(theme(page)).toHaveAttribute('data-theme', 'light');
  expect(await page.evaluate(() => localStorage.getItem('sealcode.theme'))).toBe('light');
  for (const path of ['/', '/about.html', '/admin/login', '/admin']) {
    await page.goto(path);
    await expect(theme(page)).toHaveAttribute('data-theme', 'light');
    await expect(toggle(page)).toHaveAccessibleName('Switch to dark theme');
  }
  await page.reload();
  await expect(theme(page)).toHaveAttribute('data-theme', 'light');
  await toggle(page).focus();
  await page.keyboard.press('Space');
  await expect(theme(page)).toHaveAttribute('data-theme', 'dark');
});

test('saved theme is applied before the stylesheet finishes loading', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('sealcode.theme', 'light'));
  let release;
  const stylesReady = new Promise((done) => { release = done; });
  await page.route('**/site.css*', async (route) => { await stylesReady; await route.continue(); });
  try {
    await page.goto('/', { waitUntil: 'commit' });
    await expect(theme(page)).toHaveAttribute('data-theme', 'light');
  } finally { release(); }
  await page.waitForLoadState('load');
});

test('blocked storage falls back to dark and still allows a temporary theme choice', async ({ page }) => {
  const errors = [];
  page.on('pageerror', (error) => errors.push(error.message));
  await page.addInitScript(() => Object.defineProperty(window, 'localStorage', {
    get() { throw new DOMException('Blocked', 'SecurityError'); }
  }));
  await page.goto('/');
  await expect(theme(page)).toHaveAttribute('data-theme', 'dark');
  await toggle(page).click();
  await expect(theme(page)).toHaveAttribute('data-theme', 'light');
  await page.reload();
  await expect(theme(page)).toHaveAttribute('data-theme', 'dark');
  expect(errors).toEqual([]);
});

test('theme changes and preference removal synchronize between tabs', async ({ page, context }) => {
  await page.goto('/');
  const other = await context.newPage();
  await other.goto('/about.html');
  await toggle(page).click();
  await expect(theme(other)).toHaveAttribute('data-theme', 'light');
  await other.evaluate(() => localStorage.removeItem('sealcode.theme'));
  await expect(theme(page)).toHaveAttribute('data-theme', 'dark');
  await other.close();
});

test('home form rejects invalid links and joins a valid room with the keyboard under CSP', async ({ page, request }) => {
  const created = await request.post('/admin/rooms', {
    headers: await csrf(request), data: { name: 'Keyboard entry', language: 'csharp' }
  });
  const roomPath = `/room/${(await created.json()).RoomId}`;
  const violations = [];
  page.on('console', (message) => { if (message.type() === 'error') violations.push(message.text()); });
  await page.goto('/');
  await page.locator('#roomLink').fill('javascript:alert(1)');
  await page.locator('#roomLink').press('Enter');
  await expect(page.locator('#roomLinkError')).toBeVisible();
  await page.locator('#roomLink').fill(roomPath);
  await expect(page.locator('#roomLinkError')).toBeHidden();
  await page.locator('#roomLink').press('Enter');
  await expect(page).toHaveURL(new RegExp(roomPath));
  await page.locator('#displayNameInput').fill('Keyboard user');
  await page.locator('#displayNameInput').press('Enter');
  await expect(page.locator('#joinOverlay')).toBeHidden();
  await expect(page.locator('#connectionStatus')).toHaveText('connected');
  expect(violations).toEqual([]);
});

test('join controls wait for the editor bundle before accepting input', async ({ page, request }) => {
  const created = await request.post('/admin/rooms', {
    headers: await csrf(request), data: { name: 'Slow editor load', language: 'csharp' }
  });
  const roomPath = `/room/${(await created.json()).RoomId}`;
  let release;
  const editorReady = new Promise((done) => { release = done; });
  await page.route('**/assets/room.js?*', async (route) => { await editorReady; await route.continue(); });
  try {
    await page.goto(roomPath, { waitUntil: 'commit' });
    await expect(page.locator('#displayNameInput')).toBeDisabled();
    await expect(page.locator('#joinBtn')).toBeDisabled();
    await expect(page.locator('#joinBtn')).toContainText('Loading editor');
  } finally { release(); }
  await page.locator('#displayNameInput').fill('Patient programmer');
  await page.locator('#displayNameInput').press('Enter');
  await expect(page.locator('#joinOverlay')).toBeHidden();
  await expect(page).toHaveURL(new RegExp(roomPath));
});

test('Monaco changes theme without losing text, selection, model or undo', async ({ page, request }, testInfo) => {
  const created = await request.post('/admin/rooms', {
    headers: await csrf(request), data: { name: 'Design review', language: 'javascript' }
  });
  await page.goto('/');
  await toggle(page).click();
  await page.goto(`/room/${(await created.json()).RoomId}`);
  await expect(theme(page)).toHaveAttribute('data-theme', 'light');
  await page.locator('#displayNameInput').fill('Nikita');
  await page.locator('#displayNameInput').press('Enter');
  await expect(page.locator('#joinOverlay')).toBeHidden();
  await expect(page.locator('.monaco-editor').first()).toHaveCSS('background-color', 'rgb(255, 255, 255)');
  await page.evaluate(() => monaco.editor.getEditors()[0].focus());
  await page.keyboard.insertText('const answer = 42;');
  await expect.poll(() => page.evaluate(() => monaco.editor.getModels()[0].getValue())).toBe('const answer = 42;');
  await page.evaluate(() => {
    window.appearanceModel = monaco.editor.getModels()[0];
    monaco.editor.getEditors()[0].setSelection({ startLineNumber: 1, startColumn: 7, endLineNumber: 1, endColumn: 13 });
  });
  const selection = await page.evaluate(() => ({ ...monaco.editor.getEditors()[0].getSelection() }));
  await page.screenshot({ path: testInfo.outputPath('room-light.png'), fullPage: true });
  await toggle(page).click();
  await expect(page.locator('.monaco-editor').first()).toHaveCSS('background-color', 'rgb(20, 26, 35)');
  expect(await page.evaluate(() => monaco.editor.getModels()[0] === window.appearanceModel)).toBe(true);
  expect(await page.evaluate(() => monaco.editor.getModels()[0].getValue())).toBe('const answer = 42;');
  expect(await page.evaluate(() => ({ ...monaco.editor.getEditors()[0].getSelection() }))).toEqual(selection);
  await page.screenshot({ path: testInfo.outputPath('room-dark.png'), fullPage: true });
  await page.evaluate(() => monaco.editor.getEditors()[0].focus());
  await page.keyboard.press('Control+z');
  await expect.poll(() => page.evaluate(() => monaco.editor.getModels()[0].getValue())).toBe('');
  await page.setViewportSize({ width: 375, height: 812 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('all page layouts fit desktop and mobile in both themes', async ({ page }, testInfo) => {
  const errors = [];
  page.on('pageerror', (error) => errors.push(error.message));
  for (const appearance of ['dark', 'light']) {
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto('/');
    if (await theme(page).getAttribute('data-theme') !== appearance) await toggle(page).click();
    await page.screenshot({ path: testInfo.outputPath(`home-${appearance}.png`), animations: 'disabled', fullPage: true });
    for (const path of ['/admin/login', '/admin', '/about.html', '/']) {
      await page.setViewportSize({ width: 1440, height: 900 });
      await page.goto(path);
      await expect(theme(page)).toHaveAttribute('data-theme', appearance);
      await expect(page.locator('main')).toBeVisible();
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), path).toBe(true);
      const name = path === '/' ? 'home' : path.replaceAll('/', '-').replace('.html', '').slice(1);
      await page.screenshot({ path: testInfo.outputPath(`${name}-${appearance}.png`), animations: 'disabled', fullPage: true });
      await page.setViewportSize({ width: 375, height: 812 });
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), path).toBe(true);
      await page.screenshot({ path: testInfo.outputPath(`${name}-mobile-${appearance}.png`), animations: 'disabled', fullPage: true });
    }
    await page.screenshot({ path: testInfo.outputPath(`home-mobile-${appearance}.png`), animations: 'disabled', fullPage: true });
  }
  expect(errors).toEqual([]);
});
