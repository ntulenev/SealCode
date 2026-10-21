import { csrf } from './admin-token.js';
import { expect, test } from './fixtures.js';
import * as Y from 'yjs';

async function createAndJoin(page) {
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
  await page.evaluate(
    (size) =>
      monaco.editor
        .getModels()[0]
        .applyEdits([{ range: new monaco.Range(1, 1, 1, 1), text: 'x'.repeat(size) }]),
    size
  );
}

test('large documents checkpoint without disconnecting and retain working undo', async ({
  page
}) => {
  const roomId = await createAndJoin(page);
  await insertText(page, 500000);

  await expect
    .poll(() =>
      page.evaluate(
        async (roomId) =>
          (await probe.invoke('JoinRoom', roomId, 'Probe')).CheckpointRevision,
        roomId
      )
    )
    .toBeGreaterThan(0);
  const state = await page.evaluate(
    (roomId) => probe.invoke('JoinRoom', roomId, 'Probe'),
    roomId
  );
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
  await expect
    .poll(() => page.evaluate(() => monaco.editor.getModels()[0].getValueLength()))
    .toBe(0);
  await page.evaluate(() => probe.stop());
});

test('oversized checkpoints are skipped without retries or lost incremental changes', async ({
  page
}) => {
  await page.route('**/features', (route) =>
    route.fulfill({
      json: { CollaborativeUndoEnabled: true, MaxYjsMessageBytes: 4096 }
    })
  );
  let checkpointMessages = 0;
  page.on('websocket', (socket) =>
    socket.on('framesent', ({ payload }) => {
      if (typeof payload === 'string' && payload.includes('"target":"CheckpointYjs"'))
        checkpointMessages++;
    })
  );
  const roomId = await createAndJoin(page);
  // Grow the journal to the checkpoint threshold without exceeding each edit limit.
  for (let revision = 0; revision < 64; revision++) {
    await insertText(page, revision === 0 ? 2000 : 1);
    await expect
      .poll(() =>
        page.evaluate(
          async (roomId) =>
            (await probe.invoke('GetRoomRevisions', roomId)).DocumentRevision,
          roomId
        )
      )
      .toBe(revision + 1);
  }

  await expect(page.locator('#syncError')).toContainText('too large to save a snapshot');
  // Wait through more than two checkpoint timer intervals to catch a retry loop.
  await page.waitForTimeout(1000);
  expect(checkpointMessages).toBe(0);
  await expect(page.locator('#connectionStatus')).toHaveText('connected');
  const state = await page.evaluate(
    (roomId) => probe.invoke('JoinRoom', roomId, 'Probe'),
    roomId
  );
  expect(state.DocumentRevision).toBeGreaterThan(0);
  expect(state.CheckpointRevision).toBe(0);
  const doc = new Y.Doc();
  for (const update of state.YjsUpdates)
    Y.applyUpdate(doc, Buffer.from(update.Update, 'base64'));
  expect(doc.getText('monaco').toString()).toBe('x'.repeat(2063));
  doc.destroy();
  await page.evaluate(() => probe.stop());
});

test('oversized edits remain local and visible without retrying the transport', async ({
  page
}) => {
  await page.route('**/features', (route) =>
    route.fulfill({
      json: { CollaborativeUndoEnabled: true, MaxYjsMessageBytes: 4096 }
    })
  );
  let updateMessages = 0;
  page.on('websocket', (socket) =>
    socket.on('framesent', ({ payload }) => {
      if (typeof payload === 'string' && payload.includes('"target":"UpdateYjs"'))
        updateMessages++;
    })
  );
  const roomId = await createAndJoin(page);
  await insertText(page, 5000);

  await expect(page.locator('#syncError')).toContainText('some changes have not synced');
  await page.waitForTimeout(1000);
  expect(updateMessages).toBe(0);
  expect(await page.evaluate(() => monaco.editor.getModels()[0].getValueLength())).toBe(
    5000
  );
  await expect(page.locator('#connectionStatus')).toHaveText('connected');
  const state = await page.evaluate(
    (roomId) => probe.invoke('JoinRoom', roomId, 'Probe'),
    roomId
  );
  expect(state.DocumentRevision).toBe(0);
  await page.evaluate(() => probe.stop());
});

test('only the latest author checkpoints after the revision threshold', async ({
  page,
  context
}) => {
  const counts = [0, 0];
  const countMessages = (target, index) =>
    target.on('websocket', (socket) =>
      socket.on('framesent', ({ payload }) => {
        if (typeof payload === 'string' && payload.includes('"target":"CheckpointYjs"'))
          counts[index]++;
      })
    );
  countMessages(page, 0);
  const roomId = await createAndJoin(page);
  const observer = await context.newPage();
  countMessages(observer, 1);
  await observer.goto(`/room/${roomId}`);
  await observer.locator('#displayNameInput').fill('Bob');
  await observer.locator('#joinBtn').click();
  await expect(observer.locator('#joinOverlay')).toHaveClass(/hidden/);
  for (let revision = 1; revision <= 65; revision++) {
    await insertText(page, 1);
    await expect
      .poll(() =>
        page.evaluate(
          async (roomId) =>
            (await probe.invoke('GetRoomRevisions', roomId)).DocumentRevision,
          roomId
        )
      )
      .toBe(revision);
    if (revision === 20) {
      await page.waitForTimeout(1200);
      expect(counts).toEqual([0, 0]);
    }
  }
  await expect
    .poll(() =>
      page.evaluate(
        async (roomId) =>
          (await probe.invoke('JoinRoom', roomId, 'Probe')).CheckpointRevision,
        roomId
      )
    )
    .toBeGreaterThan(0);
  expect(counts[0]).toBeGreaterThan(0);
  expect(counts[1]).toBe(0);
  await observer.close();
  await page.evaluate(() => probe.stop());
});

function countCheckpoints(page) {
  let count = 0;
  page.on('websocket', (socket) =>
    socket.on('framesent', ({ payload }) => {
      if (typeof payload === 'string' && payload.includes('"target":"CheckpointYjs"')) {
        count++;
      }
    })
  );
  return () => count;
}

async function joinObserver(context, roomId) {
  const observer = await context.newPage();
  const checkpointCount = countCheckpoints(observer);
  await observer.goto(`/room/${roomId}`);
  await observer.locator('#displayNameInput').fill('Bob');
  await observer.locator('#joinBtn').click();
  await expect(observer.locator('#joinOverlay')).toHaveClass(/hidden/);
  return { observer, checkpointCount };
}

async function revisions(page, roomId) {
  return page.evaluate((roomId) => probe.invoke('GetRoomRevisions', roomId), roomId);
}

async function syncState(page, roomId) {
  return page.evaluate((roomId) => probe.invoke('SyncYjs', roomId), roomId);
}

async function appendRevisions(author, probePage, roomId, first, last) {
  for (let revision = first; revision <= last; revision++) {
    await insertText(author, 1);
    await expect
      .poll(async () => (await revisions(probePage, roomId)).DocumentRevision)
      .toBe(revision);
  }
}

async function suppressAuthorCheckpoints(page) {
  await page.routeWebSocket('**/roomHub*', (route) => {
    const server = route.connectToServer();
    route.onMessage((message) => {
      if (typeof message !== 'string') return server.send(message);
      for (const record of message.split('\x1e').filter(Boolean)) {
        const frame = JSON.parse(record);
        if (frame.target === 'CheckpointYjs') {
          route.send(
            JSON.stringify({
              type: 3,
              invocationId: frame.invocationId,
              result: false
            }) + '\x1e'
          );
        } else {
          server.send(record + '\x1e');
        }
      }
    });
  });
}

async function startProbe(page, roomId) {
  await page.evaluate(async (roomId) => {
    window.probe = new signalR.HubConnectionBuilder().withUrl('/roomHub').build();
    await probe.start();
    await probe.invoke('JoinRoom', roomId, 'Probe');
  }, roomId);
}

test('observer accounts for checkpoint broadcasts across another fallback window', async ({
  page,
  context
}) => {
  test.setTimeout(120_000);
  const authorCount = countCheckpoints(page);
  const roomId = await createAndJoin(page);
  const { observer, checkpointCount: observerCount } = await joinObserver(
    context,
    roomId
  );
  await appendRevisions(page, page, roomId, 1, 64);
  await expect
    .poll(async () => (await syncState(page, roomId)).CheckpointRevision)
    .toBe(64);
  await appendRevisions(page, page, roomId, 65, 64 + 127);
  await page.waitForTimeout(1500);
  expect(authorCount()).toBeGreaterThan(0);
  expect(observerCount()).toBe(0);
  await observer.close();
  await page.evaluate(() => probe.stop());
});

test('observer becomes latest author and checkpoints after the previous author leaves', async ({
  page,
  context
}) => {
  await suppressAuthorCheckpoints(page);
  const roomId = await createAndJoin(page);
  const { observer, checkpointCount: observerCount } = await joinObserver(
    context,
    roomId
  );
  await appendRevisions(page, page, roomId, 1, 80);
  expect((await syncState(page, roomId)).CheckpointRevision).toBe(0);
  expect(observerCount()).toBe(0);
  await page.evaluate(() => probe.stop());
  await page.close();
  await startProbe(observer, roomId);
  await appendRevisions(observer, observer, roomId, 81, 81);
  await expect
    .poll(async () => (await syncState(observer, roomId)).CheckpointRevision)
    .toBe(81);
  expect(observerCount()).toBe(1);
  await observer.evaluate(() => probe.stop());
  await observer.close();
});

test('passive observer checkpoints at the doubled threshold when the author leaves', async ({
  page,
  context
}) => {
  await suppressAuthorCheckpoints(page);
  const roomId = await createAndJoin(page);
  const { observer, checkpointCount: observerCount } = await joinObserver(
    context,
    roomId
  );
  await appendRevisions(page, page, roomId, 1, 80);
  const state = await page.evaluate((roomId) => probe.invoke('SyncYjs', roomId), roomId);
  expect(state.CheckpointRevision).toBe(0);
  await page.evaluate(() => probe.stop());
  await page.close();
  await startProbe(observer, roomId);
  const doc = new Y.Doc();
  for (const update of state.YjsUpdates) {
    Y.applyUpdate(doc, Buffer.from(update.Update, 'base64'));
  }
  for (let revision = 81; revision <= 128; revision++) {
    let payload;
    const capture = (update) => {
      payload = Buffer.from(update).toString('base64');
    };
    doc.on('update', capture);
    doc.getText('monaco').insert(0, 'x');
    doc.off('update', capture);
    await observer.evaluate(
      ({ roomId, payload }) => probe.invoke('UpdateYjs', roomId, payload),
      { roomId, payload }
    );
    await expect
      .poll(async () => (await revisions(observer, roomId)).DocumentRevision)
      .toBe(revision);
    if (revision === 127) {
      await observer.waitForTimeout(1500);
      expect(observerCount()).toBe(0);
      expect((await syncState(observer, roomId)).CheckpointRevision).toBe(0);
    }
  }
  await expect
    .poll(async () => (await syncState(observer, roomId)).CheckpointRevision)
    .toBe(128);
  expect(observerCount()).toBe(1);
  doc.destroy();
  await observer.evaluate(() => probe.stop());
  await observer.close();
});
