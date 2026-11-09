import { test as base, expect } from './fixtures.js';
import { csrf } from './admin-token.js';

const test = base.extend({
  whiteboard: async ({ browser, request }, use, testInfo) => {
    const response = await request.post('/admin/rooms', {
      headers: await csrf(request), data: { name: testInfo.title.slice(0, 160), kind: 'whiteboard' }
    });
    expect(response.ok()).toBe(true);
    const { RoomId: id } = await response.json();
    const path = `/room/${id}`;
    const contexts = await Promise.all([0, 1].map(() => browser.newContext({ viewport: { width: 1280, height: 900 } })));
    const errors = [];
    async function newPage(context) {
      const page = await context.newPage();
      page.on('pageerror', (error) => errors.push(error.message));
      return page;
    }
    try {
      const alice = await newPage(contexts[0]);
      const bob = await newPage(contexts[1]);
      await use({
        alice, bob,
        async join() {
          await join(alice, path, 'Alice');
          await join(bob, path, 'Bob');
          await expect(alice.locator('#participantsList li')).toHaveCount(2);
        },
        async observe() {
          const observer = await newPage(contexts[0]);
          await join(observer, path, 'Observer');
          return observer;
        }
      });
      expect(errors, 'Real drawing and recovery must not produce browser errors').toEqual([]);
    } finally {
      await Promise.all(contexts.map((context) => context.close()));
      const deleted = await request.delete(`/admin/rooms/${id}`, { headers: await csrf(request) });
      expect(deleted.ok()).toBe(true);
    }
  }
});

test.use({ actionTimeout: 10000 });

async function join(page, path, name) {
  await page.goto(path);
  await page.locator('#displayNameInput').fill(name);
  await page.locator('#joinBtn').click();
  await expect(page.locator('#joinOverlay')).toBeHidden();
  await expect(page.locator('#connectionStatus')).toHaveText('connected');
}

async function drag(page, x, y, dx, dy) {
  await page.mouse.move(x, y);
  await page.mouse.down();
  await page.mouse.move(x + dx, y + dy, { steps: 4 });
  await page.mouse.up();
}

async function draw(page, type, x, y, width = 160, height = 100) {
  await page.bringToFront();
  await page.locator(`button[data-tool="${type}"]`).click();
  const canvas = await page.locator('.board-canvas').boundingBox();
  await drag(page, canvas.x + x, canvas.y + y, width, height);
}

function shape(page, id) {
  return page.locator(`[data-shape-id="${id}"]`);
}

async function select(page, id) {
  await page.bringToFront();
  await page.locator('button[data-tool="select"]').click();
  await shape(page, id).click();
}

async function label(page, id, value) {
  await select(page, id);
  await page.getByRole('button', { name: 'Edit label', exact: true }).click();
  const input = page.getByRole('textbox', { name: 'Element label' });
  await input.fill(value);
  await input.press('Enter');
}

async function move(page, id, dx, dy) {
  await page.bringToFront();
  await page.locator('button[data-tool="select"]').click();
  const box = await shape(page, id).boundingBox();
  await drag(page, box.x + box.width / 2, box.y + box.height / 2, dx, dy);
}

async function resize(page, id, dx, dy) {
  await select(page, id);
  const handle = page.locator(`[data-resize="${id}"]`);
  await expect(handle).toBeVisible();
  const box = await handle.boundingBox();
  await drag(page, box.x + box.width / 2, box.y + box.height / 2, dx, dy);
}

async function rectangle(page, id) {
  return shape(page, id).locator('rect.board-fill').evaluate((element) =>
    Object.fromEntries(['x', 'y', 'width', 'height'].map((key) => [key, Number(element.getAttribute(key))]))
  );
}

// Compare IDs, labels and the complete rendered geometry, excluding local selection/viewport.
async function scene(page) {
  return page.locator('[data-shape-id]').evaluateAll((elements) => elements.map((element) => ({
    id: element.getAttribute('data-shape-id'), svg: element.outerHTML
  })).sort((a, b) => a.id.localeCompare(b.id)));
}

async function expectScene(pages, expected) {
  for (const page of pages) await expect.poll(() => scene(page)).toEqual(expected);
}

function records(message) {
  return typeof message === 'string'
    ? message.split('\x1e').filter(Boolean).map((data) => ({ frame: JSON.parse(data), data: data + '\x1e' }))
    : [{ frame: {}, data: message }];
}

// The two browsers commit locally against the same state before either update reaches the server.
async function updateGate(page) {
  let holding = false;
  const pending = [];
  await page.routeWebSocket('**/roomHub*', (route) => {
    const server = route.connectToServer();
    route.onMessage((message) => {
      for (const record of records(message)) {
        if (holding && record.frame.target === 'UpdateYjs') pending.push(() => server.send(record.data));
        else server.send(record.data);
      }
    });
  });
  return {
    hold() { holding = true; },
    get count() { return pending.length; },
    release() {
      holding = false;
      for (const send of pending.splice(0)) send();
    }
  };
}

test('concurrent creation and edits to different figures preserve both participants changes', async ({ whiteboard }) => {
  const { alice, bob } = whiteboard;
  const aliceGate = await updateGate(alice);
  const bobGate = await updateGate(bob);
  await whiteboard.join();
  aliceGate.hold();
  bobGate.hold();
  await draw(alice, 'rectangle', 100, 160);
  await draw(bob, 'database', 500, 180);
  await expect.poll(() => aliceGate.count).toBe(1);
  await expect.poll(() => bobGate.count).toBe(1);
  await expect(alice.locator('[data-shape-id]')).toHaveCount(1);
  await expect(bob.locator('[data-shape-id]')).toHaveCount(1);
  const aliceShape = await alice.locator('[data-shape-id]').getAttribute('data-shape-id');
  const bobShape = await bob.locator('[data-shape-id]').getAttribute('data-shape-id');
  expect(aliceShape).not.toBe(bobShape);
  const created = [...await scene(alice), ...await scene(bob)].sort((a, b) => a.id.localeCompare(b.id));
  bobGate.release();
  aliceGate.release();
  await expectScene([alice, bob], created);

  aliceGate.hold();
  bobGate.hold();
  await label(alice, aliceShape, 'Order API');
  await label(bob, bobShape, 'Orders DB');
  await expect.poll(() => aliceGate.count).toBe(1);
  await expect.poll(() => bobGate.count).toBe(1);
  await expect(shape(alice, bobShape)).toHaveText('');
  await expect(shape(bob, aliceShape)).toHaveText('');
  aliceGate.release();
  bobGate.release();
  for (const page of [alice, bob]) {
    await expect(shape(page, aliceShape)).toHaveText('Order API');
    await expect(shape(page, bobShape)).toHaveText('Orders DB');
  }
  const expected = await scene(alice);
  await expectScene([bob, await whiteboard.observe()], expected);
});

test('concurrent edits of the same figure converge to one complete CRDT value', async ({ whiteboard }) => {
  const { alice, bob } = whiteboard;
  const aliceGate = await updateGate(alice);
  const bobGate = await updateGate(bob);
  await whiteboard.join();
  await draw(alice, 'rectangle', 100, 160);
  await expect(bob.locator('[data-shape-id]')).toHaveCount(1);
  const id = await alice.locator('[data-shape-id]').getAttribute('data-shape-id');
  const geometry = await rectangle(alice, id);
  aliceGate.hold();
  bobGate.hold();
  await label(alice, id, 'Alice proposal');
  await label(bob, id, 'Bob proposal');
  await expect.poll(() => aliceGate.count).toBe(1);
  await expect.poll(() => bobGate.count).toBe(1);
  await expect(shape(alice, id)).toHaveText('Alice proposal');
  await expect(shape(bob, id)).toHaveText('Bob proposal');
  bobGate.release();
  aliceGate.release();
  // Yjs chooses the winner; the test must not assume a random client ID ordering.
  await expect.poll(async () => JSON.stringify(await scene(alice)) === JSON.stringify(await scene(bob))).toBe(true);
  expect(['Alice proposal', 'Bob proposal']).toContain(await shape(alice, id).textContent());
  expect(await rectangle(alice, id)).toEqual(geometry);
  await expectScene([await whiteboard.observe()], await scene(alice));
});

test('move resize and delete propagate exact geometry while preserving other figures', async ({ whiteboard }) => {
  const { alice, bob } = whiteboard;
  await whiteboard.join();
  await draw(alice, 'rectangle', 100, 160);
  await expect(bob.locator('[data-shape-id]')).toHaveCount(1);
  const id = await alice.locator('[data-shape-id]').getAttribute('data-shape-id');
  await label(alice, id, 'API');
  await draw(bob, 'database', 600, 180);
  await expect(alice.locator('[data-shape-id]')).toHaveCount(2);
  const protectedId = await bob.locator('[data-shape-type="database"]').getAttribute('data-shape-id');
  const protectedScene = (await scene(bob)).find((item) => item.id === protectedId);
  const original = await rectangle(alice, id);

  await move(alice, id, 85, 35);
  const moved = { ...original, x: original.x + 85, y: original.y + 35 };
  await expect.poll(() => rectangle(bob, id)).toEqual(moved);
  await expectScene([bob], await scene(alice));
  await resize(bob, id, 60, 40);
  const resized = { ...moved, width: moved.width + 60, height: moved.height + 40 };
  await expect.poll(() => rectangle(alice, id)).toEqual(resized);
  await expect(shape(alice, id)).toHaveText('API');
  const observer = await whiteboard.observe();
  await expect.poll(() => rectangle(observer, id)).toEqual(resized);

  await select(bob, id);
  await bob.keyboard.press('Delete');
  await expectScene([alice, bob, observer], [protectedScene]);
  await alice.reload();
  await alice.locator('#displayNameInput').fill('Alice after reload');
  await alice.locator('#joinBtn').click();
  await expect(alice.locator('#joinOverlay')).toBeHidden();
  await expectScene([alice], [protectedScene]);
});

async function interruptedUpdate(page, delivery) {
  let armed = true;
  let original;
  let cutConnection;
  let allowRejoin = false;
  const joins = [];
  let attempts = 0;
  let committed = false;
  await page.routeWebSocket('**/roomHub*', (route) => {
    const server = route.connectToServer();
    let interrupted = false;
    route.onMessage((message) => {
      for (const { frame, data } of records(message)) {
        if (cutConnection && !allowRejoin && frame.target === 'JoinRoom') {
          joins.push(() => server.send(data));
          continue;
        }
        if (frame.target === 'UpdateYjs') {
          attempts++;
          if (armed) {
            armed = false;
            interrupted = true;
            original = frame;
            cutConnection = async () => {
              await route.close({ code: 1012, reason: 'E2E connection interruption' });
              await server.close({ code: 1012, reason: 'E2E connection interruption' });
            };
            if (delivery === 'before-acceptance') continue;
          }
        }
        server.send(data);
      }
    });
    server.onMessage((message) => {
      for (const { frame, data } of records(message)) {
        if (interrupted && frame.target === 'YjsUpdated' && frame.arguments[0] === original.arguments[1]) {
          committed = true;
          continue;
        }
        if (interrupted && frame.type === 3 && frame.invocationId === original.invocationId) continue;
        route.send(data);
      }
    });
  });
  return {
    get attempts() { return attempts; },
    get committed() { return committed; },
    disconnect: () => cutConnection(),
    resume() {
      allowRejoin = true;
      for (const send of joins.splice(0)) send();
    }
  };
}

for (const delivery of ['before-acceptance', 'after-acceptance-before-ack']) {
  test(`in-flight update survives disconnect ${delivery} without lost or duplicate figures`, async ({ whiteboard }) => {
    const { alice, bob } = whiteboard;
    const fault = await interruptedUpdate(alice, delivery);
    await whiteboard.join();
    await draw(alice, 'rectangle', 100, 160);
    await expect.poll(() => fault.attempts).toBe(1);
    const id = await alice.locator('[data-shape-id]').getAttribute('data-shape-id');
    const local = await scene(alice);
    if (delivery === 'after-acceptance-before-ack') {
      await expect.poll(() => fault.committed).toBe(true);
      await expectScene([bob], local);
    } else {
      expect(fault.committed).toBe(false);
      await expect(bob.locator('[data-shape-id]')).toHaveCount(0);
    }
    expect(await alice.evaluate(() => window.__debugUndoState().documentRevision)).toBe(0);
    await fault.disconnect();
    await expect(alice.locator('#connectionStatus')).not.toHaveText('connected');
    await expect(alice.locator('button[data-tool="rectangle"]')).toBeDisabled();
    await expectScene([alice], local);

    // Rejoin is gated so Bob's edit is guaranteed to happen during Alice's outage.
    await draw(bob, 'database', 600, 180);
    const remote = await bob.locator('[data-shape-type="database"]').getAttribute('data-shape-id');
    const expected = [...local, ...(await scene(bob)).filter((item) => item.id === remote)]
      .sort((a, b) => a.id.localeCompare(b.id));
    fault.resume();
    await expect(alice.locator('#connectionStatus')).toHaveText('connected', { timeout: 20000 });
    await expect.poll(() => fault.attempts).toBeGreaterThanOrEqual(2);
    await expectScene([alice, bob], expected);
    await expect(alice.locator('#syncError')).toBeHidden();
    const observer = await whiteboard.observe();
    await expectScene([observer], expected);

    await alice.getByRole('button', { name: 'Undo', exact: true }).click();
    const onlyRemote = expected.filter((item) => item.id !== id);
    await expectScene([alice, bob, observer], onlyRemote);
    await alice.getByRole('button', { name: 'Redo', exact: true }).click();
    await expectScene([alice, bob, observer], expected);
  });
}
