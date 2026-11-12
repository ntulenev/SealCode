import { test, expect } from './fixtures.js';
import { csrf } from './admin-token.js';

async function setup(browser, isolatedHost, kind = 'code', observerSettings = {}, withGuestLink = true) {
  const { baseURL } = await isolatedHost({ ConnectionStrings: { SealCode: '' }, Observers: { Enabled: true, ...observerSettings } });
  const owner = await browser.newContext({ baseURL });
  const login = await owner.request.post('/admin/login', { headers: await csrf(owner.request), form: { name: 'BrowserAdmin', password: 'browser-pass' } });
  expect(login.ok()).toBe(true);
  const created = await owner.request.post('/admin/rooms', { headers: await csrf(owner.request), data: { Name: 'Guest presentation', Kind: kind, Language: 'sql' } });
  const { RoomId: id } = await created.json();
  let link;
  if (withGuestLink) {
    const response = await owner.request.post(`/room/${id}/observer-link`, { headers: await csrf(owner.request) });
    expect(response.ok()).toBe(true);
    link = (await response.json()).Url;
  }
  const guests = await browser.newContext({ baseURL });
  const editor = await owner.newPage();
  await editor.goto(`/room/${id}`);
  await editor.locator('#displayNameInput').fill('Presenter');
  await editor.locator('#joinBtn').click();
  await expect(editor.locator('#joinOverlay')).toBeHidden();
  const guest = await guests.newPage();
  return { owner, guests, editor, guest, id, link, baseURL };
}

test('guest code link is live, read-only, counted separately and revoked by its owner', async ({ browser, isolatedHost }) => {
  const { owner, guests, editor, guest, id, link } = await setup(browser, isolatedHost);
  try {
    const errors = [];
    guest.on('pageerror', error => errors.push(error.message));
    await guest.goto(link);
    await expect(guest.locator('#watchStatus')).toHaveText('Live · Read only');
    await expect(editor.locator('#observerCount')).toHaveText('👁 1');
    await expect(editor.locator('#participantsList li')).toHaveCount(1);
    await editor.evaluate(() => window.monaco.editor.getModels()[0].setValue('SELECT 42;'));
    await expect(guest.locator('.view-lines')).toContainText('SELECT 42;');
    await guest.screenshot({ path: 'output/observer-review/code-dark.png', fullPage: true });
    await guest.setViewportSize({ width: 390, height: 844 });
    await guest.getByRole('button', { name: 'Switch to light theme' }).click();
    await guest.screenshot({ path: 'output/observer-review/code-mobile-light.png', fullPage: true });
    await guest.locator('.monaco-editor textarea').focus();
    await guest.keyboard.type('FORBIDDEN');
    await guest.keyboard.press('Control+z');
    await expect(guest.locator('.view-lines')).not.toContainText('FORBIDDEN');
    await expect(guest.locator('#deleteRoom')).toHaveCount(0);
    await expect(guest.locator('#languageSelect')).toHaveCount(0);
    expect(await guest.content()).not.toContain(id);
    const insecure = await guests.request.post(`/room/${id}/observer-link`);
    expect(insecure.ok()).toBe(false);
    const forbidden = await guests.request.get(`/room/${id}/observer-link`);
    expect(forbidden.status()).toBe(401);
    await guest.reload();
    await expect(guest.locator('#watchStatus')).toHaveText('Live · Read only');
    await expect(guest.locator('.view-lines')).toContainText('SELECT 42;');
    const revoke = await owner.request.delete(`/room/${id}/observer-link`, { headers: await csrf(owner.request) });
    expect(revoke.ok()).toBe(true);
    await expect(editor.locator('#observerCount')).toBeHidden();
    await expect(guest.locator('#watchStatus')).not.toHaveText('Live · Read only');
    await guest.reload();
    await expect(guest.locator('#watchStatus')).toContainText('unavailable');
    expect(errors).toEqual([]);
  } finally { await guests.close(); await owner.close(); }
});

test('whiteboard guest receives shapes and can pan without drawing or editing labels', async ({ browser, isolatedHost }) => {
  const { owner, guests, editor, guest, link } = await setup(browser, isolatedHost, 'whiteboard');
  try {
    await guest.goto(link);
    await expect(guest.locator('#watchStatus')).toHaveText('Live · Read only');
    await editor.locator('[data-tool="rectangle"]').click();
    const box = await editor.locator('.board-canvas').boundingBox();
    await editor.mouse.move(box.x + 140, box.y + 180);
    await editor.mouse.down(); await editor.mouse.move(box.x + 260, box.y + 250); await editor.mouse.up();
    await expect(guest.locator('[data-shape-id]')).toHaveCount(1);
    await guest.screenshot({ path: 'output/observer-review/whiteboard-dark.png', fullPage: true });
    await expect(guest.locator('[data-tool="rectangle"]')).toBeDisabled();
    await expect(guest.locator('[data-tool="pan"]')).toBeEnabled();
    await guest.locator('.board-canvas').focus();
    await guest.keyboard.press('Control+a'); await guest.keyboard.press('Delete'); await guest.keyboard.press('Enter');
    await expect(guest.getByRole('textbox', { name: 'Element label' })).toHaveCount(0);
    await expect(guest.locator('[data-shape-id]')).toHaveCount(1);
    await expect(editor.locator('[data-shape-id]')).toHaveCount(1);
  } finally { await guests.close(); await owner.close(); }
});

test('guest link buttons follow creation, reload and revocation without copying an empty link', async ({ browser, isolatedHost }) => {
  const { owner, guests, editor } = await setup(browser, isolatedHost, 'code', {}, false);
  try {
    const copy = editor.getByText('Copy link', { exact: true });
    const disable = editor.getByRole('button', { name: 'Disable guest access', includeHidden: true });
    const input = editor.getByRole('textbox', { name: 'Read-only guest link', includeHidden: true });
    await editor.evaluate(() => {
      window.copiedLinks = [];
      Object.defineProperty(navigator, 'clipboard', { configurable: true,
        value: { writeText: async value => window.copiedLinks.push(value) } });
    });
    await editor.getByText('Guest link', { exact: true }).click();
    for (const width of [1440, 390]) {
      await editor.setViewportSize({ width, height: 900 });
      await expect(copy).toBeHidden(); await expect(copy).toBeDisabled();
      await expect(disable).toBeHidden(); await expect(disable).toBeDisabled();
      await expect(input).toBeHidden();
      await editor.screenshot({ path: `output/observer-review/no-guest-link-${width}.png`, fullPage: true });
    }
    await copy.dispatchEvent('click');
    expect(await editor.evaluate(() => window.copiedLinks)).toEqual([]);
    await expect(editor.getByText('Guest link copied.', { exact: true })).toHaveCount(0);
    editor.on('dialog', dialog => dialog.accept());
    await editor.getByRole('button', { name: 'Create / replace link' }).click();
    await expect(copy).toBeVisible(); await expect(copy).toBeEnabled();
    await expect(disable).toBeVisible(); await expect(disable).toBeEnabled();
    await copy.click();
    expect(await editor.evaluate(() => window.copiedLinks)).toEqual([await input.inputValue()]);
    await editor.reload();
    await editor.locator('#displayNameInput').fill('Presenter');
    await editor.locator('#joinBtn').click();
    await expect(editor.locator('#joinOverlay')).toBeHidden();
    await editor.getByText('Guest link', { exact: true }).click();
    await expect(copy).toBeHidden(); await expect(copy).toBeDisabled();
    await expect(disable).toBeVisible(); await expect(disable).toBeEnabled();
    await disable.click();
    await expect(editor.getByText('Guest access disabled.', { exact: true })).toBeVisible();
    await expect(copy).toBeHidden(); await expect(disable).toBeHidden();
    await editor.reload();
    await editor.locator('#displayNameInput').fill('Presenter');
    await editor.locator('#joinBtn').click();
    await expect(editor.locator('#joinOverlay')).toBeHidden();
    await editor.getByText('Guest link', { exact: true }).click();
    await expect(copy).toBeHidden(); await expect(disable).toBeHidden();
  } finally { await guests.close(); await owner.close(); }
});

test('owner can generate a distinct guest link from the room UI', async ({ browser, isolatedHost }) => {
  const { owner, guests, editor, guest, id } = await setup(browser, isolatedHost);
  try {
    await editor.setViewportSize({ width: 390, height: 844 });
    await editor.getByText('Guest link', { exact: true }).click();
    editor.on('dialog', dialog => dialog.accept());
    await editor.getByRole('button', { name: 'Create / replace link' }).click();
    const input = editor.getByRole('textbox', { name: 'Read-only guest link' });
    await expect(input).toBeVisible();
    const bounds = await input.boundingBox();
    expect(bounds.x).toBeGreaterThanOrEqual(16);
    expect(bounds.x + bounds.width).toBeLessThanOrEqual(374);
    const link = await input.inputValue();
    expect(link).toContain('/watch#'); expect(link).not.toContain(id);
    await guest.goto(link);
    await expect(guest.locator('#watchStatus')).toHaveText('Live · Read only');
    await editor.getByRole('button', { name: 'Disable guest access' }).click();
    await expect(editor.getByText('Guest access disabled.', { exact: true })).toBeVisible();
    await expect(editor.locator('#observerCount')).toBeHidden();
  } finally { await guests.close(); await owner.close(); }
});

for (const scope of ['room', 'server']) {
  test(`guest ${scope} capacity rejection remains visible while reconnecting`, async ({ browser, isolatedHost }) => {
    const maxConnections = scope === 'room' ? 2 : 1;
    const { owner, guests, guest, link } = await setup(browser, isolatedHost, 'code', {
      MaxPerRoom: 1, MaxConnections: maxConnections, MaxConnectionsPerSource: maxConnections
    });
    try {
      await guest.goto(link);
      await expect(guest.locator('#watchStatus')).toHaveText('Live · Read only');
      const rejected = await guests.newPage();
      await rejected.goto(link);
      await expect(rejected.locator('#watchStatus')).toContainText('guest limit');
      await expect(guest.locator('#watchStatus')).toHaveText('Live · Read only');
    } finally { await guests.close(); await owner.close(); }
  });
}
