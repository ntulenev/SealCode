import { csrf } from './admin-token.js';
import { expect, test } from '@playwright/test';
import * as Y from 'yjs';

async function createAndJoin(page) {
  expect((await page.request.post('/admin/login', {
    headers: await csrf(page.request),
    form: { name: 'BrowserAdmin', password: 'browser-pass' }
  })).ok()).toBeTruthy();
  const response = await page.request.post('/admin/rooms', {
    headers: await csrf(page.request),
    data: { name: 'Sync limit', language: 'csharp' }
  });
  expect(response.ok()).toBeTruthy();
  const roomId = (await response.json()).RoomId;
  await page.goto(`/room/${roomId}`);
  await page.locator('#displayNameInput').fill('Alice');
  await page.locator('#joinBtn').click();
  await expect(page.locator('#joinOverlay')).toHaveClass(/hidden/);
  await page.evaluate(async (roomId) => {
    window.probe = new signalR.HubConnectionBuilder().withUrl('/roomHub').build();
    await probe.start();
    await probe.invoke('JoinRoom', roomId, 'Probe');
  }, roomId);
  return roomId;
}

async function insertText(page, size) {
  await page.evaluate((size) => monaco.editor.getModels()[0].applyEdits([
    { range: new monaco.Range(1, 1, 1, 1), text: 'x'.repeat(size) }
  ]), size);
}

test('large documents checkpoint without disconnecting and retain working undo', async ({ page }) => {
  const roomId = await createAndJoin(page);
  await insertText(page, 500000);

  await expect.poll(() => page.evaluate(async (roomId) =>
    (await probe.invoke('JoinRoom', roomId, 'Probe')).CheckpointRevision, roomId)
  ).toBeGreaterThan(0);
  const state = await page.evaluate((roomId) => probe.invoke('JoinRoom', roomId, 'Probe'), roomId);
  expect(state.Text.length).toBe(500000);
  expect(state.YjsUpdates).toEqual([]);
  const doc = new Y.Doc();
  Y.applyUpdate(doc, Buffer.from(state.YjsState, 'base64'));
  expect(doc.getText('monaco').toString()).toBe('x'.repeat(500000));
  doc.destroy();
  await expect(page.locator('#connectionStatus')).toHaveText('connected');
  await expect(page.locator('#syncError')).toHaveClass(/hidden/);

  await page.locator('#monacoEditor').click();
  await page.keyboard.press('Control+z');
  await expect.poll(() => page.evaluate(() => monaco.editor.getModels()[0].getValueLength())).toBe(0);
  await page.evaluate(() => probe.stop());
});

test('oversized checkpoints are skipped without retries or lost incremental changes', async ({ page }) => {
  await page.route('**/features', (route) => route.fulfill({
    json: { CollaborativeUndoEnabled: true, MaxYjsMessageBytes: 4096 }
  }));
  let checkpointMessages = 0;
  page.on('websocket', (socket) => socket.on('framesent', ({ payload }) => {
    if (typeof payload === 'string' && payload.includes('"target":"CheckpointYjs"')) checkpointMessages++;
  }));
  const roomId = await createAndJoin(page);
  await insertText(page, 2000);

  await expect(page.locator('#syncError')).toContainText('too large to save a snapshot');
  // Wait through more than two checkpoint timer intervals to catch a retry loop.
  await page.waitForTimeout(1000);
  expect(checkpointMessages).toBe(0);
  await expect(page.locator('#connectionStatus')).toHaveText('connected');
  const state = await page.evaluate((roomId) => probe.invoke('JoinRoom', roomId, 'Probe'), roomId);
  expect(state.DocumentRevision).toBeGreaterThan(0);
  expect(state.CheckpointRevision).toBe(0);
  const doc = new Y.Doc();
  for (const update of state.YjsUpdates) Y.applyUpdate(doc, Buffer.from(update.Update, 'base64'));
  expect(doc.getText('monaco').toString()).toBe('x'.repeat(2000));
  doc.destroy();
  await page.evaluate(() => probe.stop());
});

test('oversized edits remain local and visible without retrying the transport', async ({ page }) => {
  await page.route('**/features', (route) => route.fulfill({
    json: { CollaborativeUndoEnabled: true, MaxYjsMessageBytes: 4096 }
  }));
  let updateMessages = 0;
  page.on('websocket', (socket) => socket.on('framesent', ({ payload }) => {
    if (typeof payload === 'string' && payload.includes('"target":"UpdateYjs"')) updateMessages++;
  }));
  const roomId = await createAndJoin(page);
  await insertText(page, 5000);

  await expect(page.locator('#syncError')).toContainText('some changes have not synced');
  await page.waitForTimeout(1000);
  expect(updateMessages).toBe(0);
  expect(await page.evaluate(() => monaco.editor.getModels()[0].getValueLength())).toBe(5000);
  await expect(page.locator('#connectionStatus')).toHaveText('connected');
  const state = await page.evaluate((roomId) => probe.invoke('JoinRoom', roomId, 'Probe'), roomId);
  expect(state.DocumentRevision).toBe(0);
  await page.evaluate(() => probe.stop());
});
