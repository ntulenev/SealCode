import { mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { chromium, expect } from '@playwright/test';
import { startIsolatedHost } from '../tests/isolated-host.js';

// Only synthetic accounts, an ephemeral content root and loopback connections.
// Never loads the developer's appsettings or a running deployment.
async function run(command, args) {
  const child = spawn(command, args, { stdio: 'inherit', windowsHide: true });
  const [code] = await once(child, 'exit');
  if (code !== 0) throw new Error(`${command} exited with ${code}`);
}
if (!process.argv.includes('--skip-build')) {
  await run(process.execPath, ['scripts/build-client.mjs']);
  await run('dotnet', ['build', 'src/SealCode/SealCode.csproj', '-p:SkipClientBuild=true']);
}
const images = resolve('docs/images');
await mkdir(images, { recursive: true });
const host = await startIsolatedHost({ Observers: { Enabled: true }, ConnectionStrings: { SealCode: '' },
  Limits: { MaxRooms: 32, MaxConnections: 160, MaxConnectionsPerSource: 20,
    UnjoinedConnectionTimeoutSeconds: 60, NewConnectionsPerSourcePerMinute: 30, InvocationsPerSecond: 100 } });
const browser = await chromium.launch();
const errors = [];
try {
  const owner = await browser.newContext({ baseURL: host.baseURL, viewport: { width: 1440, height: 900 } });
  const viewers = await browser.newContext({ baseURL: host.baseURL, viewport: { width: 1440, height: 900 } });
  async function page(context) {
    const result = await context.newPage(); result.on('pageerror', error => errors.push(error.message)); return result;
  }
  async function csrf() { return { 'X-CSRF-TOKEN': (await (await owner.request.get('/admin/token')).json()).Token }; }
  const login = await owner.request.post('/admin/login', { headers: await csrf(), form: { name: 'BrowserAdmin', password: 'browser-pass' } });
  expect(login.ok()).toBe(true);
  async function create(name, kind) {
    const response = await owner.request.post('/admin/rooms', { headers: await csrf(), data: { Name: name, Kind: kind, Language: 'javascript' } });
    expect(response.ok()).toBe(true); return (await response.json()).RoomId;
  }
  async function join(p, id, name) {
    await p.goto(`/room/${id}`); await p.locator('#displayNameInput').fill(name); await p.locator('#joinBtn').click();
    await expect(p.locator('#joinOverlay')).toBeHidden();
  }
  async function watch(id, existingLink) {
    const response = existingLink ? null : await owner.request.post(`/room/${id}/observer-link`, { headers: await csrf() });
    const link = existingLink || (await response.json()).Url;
    const guests = await Promise.all(Array.from({ length: 3 }, async () => {
      const p = await page(viewers); await p.goto(link); await expect(p.locator('#watchStatus')).toHaveText('Live · Read only'); return p;
    }));
    return guests[0];
  }
  async function theme(p, value) {
    if (await p.locator('html').getAttribute('data-theme') !== value) await p.locator('[data-theme-toggle]').first().click();
  }
  async function shot(p, name, selector) {
    expect(await p.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    const capture = selector ? p.locator(selector) : p;
    await capture.screenshot({ path: resolve(images, name), fullPage: !selector, animations: 'disabled' });
    console.log(`Captured docs/images/${name}`);
  }
  const id = await create('Order pipeline — live walkthrough', 'code');
  const presenter = await page(owner); await join(presenter, id, 'Maya');
  const peer = await page(viewers); await join(peer, id, 'Alex');
  const code = [
    '// One room. A small team editing, a wider audience watching.',
    'const order = { id: "ORD-2048", status: "created" };', '',
    'async function processOrder(order) {',
    '  const payment = await payments.authorize(order);',
    '  await inventory.reserve(order.id);',
    '  await events.publish("OrderConfirmed", {',
    '    orderId: order.id,', '    paymentId: payment.id', '  });',
    '  return { ...order, status: "confirmed" };', '}', '',
    '// Guest links share this live view without adding editor places.'
  ].join('\n');
  await presenter.evaluate(text => window.monaco.editor.getModels()[0].setValue(text), code);
  await presenter.getByText('Guest link', { exact: true }).click();
  presenter.on('dialog', dialog => dialog.accept());
  await presenter.getByRole('button', { name: 'Create / replace link' }).click();
  const linkInput = presenter.getByRole('textbox', { name: 'Read-only guest link' });
  await expect(linkInput).toBeVisible();
  const guest = await watch(id, await linkInput.inputValue());
  await expect(guest.locator('.view-lines')).toContainText('processOrder');
  await expect(presenter.locator('#observerCount')).toHaveText('👁 3');
  await shot(presenter, 'observer-link-dark.png');
  await presenter.getByText('Guest link', { exact: true }).click();
  await shot(presenter, 'observer-owner-dark.png');
  await theme(guest, 'light'); await shot(guest, 'observer-code-light.png');
  await guest.setViewportSize({ width: 390, height: 844 }); await shot(guest, 'observer-mobile-light.png');
  const boardId = await create('System design — presentation', 'whiteboard');
  const board = await page(owner); await join(board, boardId, 'Maya');
  async function rectangle(x, label) {
    await board.locator('[data-tool="rectangle"]').click();
    const box = await board.locator('.board-canvas').boundingBox();
    await board.mouse.move(box.x + x, box.y + 200); await board.mouse.down();
    await board.mouse.move(box.x + x + 230, box.y + 310); await board.mouse.up();
    await board.keyboard.press('Enter');
    await board.getByRole('textbox', { name: 'Element label' }).fill(label);
    await board.getByRole('textbox', { name: 'Element label' }).press('Enter');
  }
  await rectangle(100, 'API gateway'); await rectangle(470, 'Order service'); await rectangle(840, 'Event stream');
  const boardGuest = await watch(boardId);
  await expect(boardGuest.locator('[data-shape-id]')).toHaveCount(3);
  await theme(boardGuest, 'dark'); await shot(boardGuest, 'observer-whiteboard-dark.png');
  const limits = await page(owner); await limits.goto('/admin/limits');
  await expect(limits.locator('#observerLimits dd').nth(1)).toHaveText('20');
  await expect(limits.locator('#observerLimits dd').nth(2)).toHaveText('40');
  await theme(limits, 'light'); await shot(limits, 'observer-limits-light.png', '[aria-labelledby="observers-title"]');
  await limits.setViewportSize({ width: 390, height: 844 }); await shot(limits, 'observer-limits-mobile-light.png', '[aria-labelledby="observers-title"]');
  expect(errors).toEqual([]);
} finally {
  await browser.close(); await host.stop();
}
