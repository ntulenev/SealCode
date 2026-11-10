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

test('participant cursors show names and stable colors in board coordinates across zoom and pan', async ({ whiteboard }, testInfo) => {
  const { alice, bob } = whiteboard;
  await whiteboard.join();
  const before = await alice.evaluate(() => window.__debugUndoState());
  await alice.bringToFront();
  const box = await alice.locator('.board-canvas').boundingBox();
  await alice.mouse.move(box.x + 220, box.y + 180);
  const cursor = bob.locator('[data-cursor-name="Alice"]');
  await expect(cursor).toBeVisible();
  await expect(cursor.locator('text')).toHaveText('Alice');
  await expect(alice.locator('[data-cursor-name="Alice"]')).toHaveCount(0);
  await expect.poll(() => cursor.evaluate((el) => el.style.transform)).toBe('translate(220px, 180px)');
  const color = await cursor.locator('path').getAttribute('fill');

  // DOM activation preserves the sender's focus and stationary pointer.
  await bob.getByRole('button', { name: 'Zoom in', exact: true }).evaluate((el) => el.click());
  const size = await bob.locator('.board-canvas').boundingBox();
  const expectedX = 220 * 1.2 - size.width * 0.1;
  const expectedY = 180 * 1.2 - size.height * 0.1;
  await expect.poll(() => cursor.evaluate((el) => {
    const matrix = new DOMMatrix(getComputedStyle(el).transform);
    return { x: Math.round(matrix.e), y: Math.round(matrix.f) };
  })).toEqual({ x: Math.round(expectedX), y: Math.round(expectedY) });
  await bob.locator('.board-canvas').dispatchEvent('wheel', { deltaX: 30, deltaY: 40 });
  await expect.poll(() => cursor.evaluate((el) => {
    const matrix = new DOMMatrix(getComputedStyle(el).transform);
    return { x: Math.round(matrix.e), y: Math.round(matrix.f) };
  })).toEqual({ x: Math.round(expectedX - 30), y: Math.round(expectedY - 40) });
  await expect(cursor.locator('path')).toHaveAttribute('fill', color);
  await bob.screenshot({ path: testInfo.outputPath('participant-cursor.png') });

  await alice.locator('.board-canvas').dispatchEvent('wheel', { deltaX: 20, deltaY: 10 });
  await expect.poll(() => cursor.evaluate((el) => {
    const matrix = new DOMMatrix(getComputedStyle(el).transform);
    return { x: Math.round(matrix.e), y: Math.round(matrix.f) };
  })).toEqual({ x: Math.round(240 * 1.2 - size.width * 0.1 - 30), y: Math.round(190 * 1.2 - size.height * 0.1 - 40) });
  expect(await alice.evaluate(() => window.__debugUndoState())).toEqual(before);
  await expectScene([alice, bob], []);

  await alice.mouse.move(0, 0);
  await expect(cursor).toHaveCount(0);
  await alice.mouse.move(box.x + 220, box.y + 180);
  await expect(cursor).toBeVisible();
  await alice.close();
  await expect(bob.locator('#participantsList li')).toHaveCount(1);
  await expect(cursor).toHaveCount(0);
});

test('participant cursors clear on connection loss and resume after rejoining', async ({ whiteboard }) => {
  const { alice, bob } = whiteboard;
  await whiteboard.join();
  await alice.bringToFront();
  const box = await alice.locator('.board-canvas').boundingBox();
  await alice.mouse.move(box.x + 200, box.y + 200);
  await expect(bob.locator('[data-cursor-name="Alice"]')).toBeVisible();
  await bob.context().setOffline(true);
  await expect(bob.locator('#connectionStatus')).not.toHaveText('connected');
  await expect(bob.locator('[data-cursor-name]')).toHaveCount(0);
  await bob.context().setOffline(false);
  await expect(bob.locator('#connectionStatus')).toHaveText('connected');
  await alice.mouse.move(box.x + 250, box.y + 240);
  await expect(bob.locator('[data-cursor-name="Alice"]')).toBeVisible();
  await expect.poll(() => bob.locator('[data-cursor-name="Alice"]').evaluate((el) => el.style.transform)).toBe('translate(250px, 240px)');
});

test('participant cursors share colors across three clients and allow drawing beneath them', async ({ whiteboard }) => {
  const { alice, bob } = whiteboard;
  await whiteboard.join();
  const observer = await whiteboard.observe();
  await alice.bringToFront();
  const aliceBox = await alice.locator('.board-canvas').boundingBox();
  const bobBox = await bob.locator('.board-canvas').boundingBox();
  await alice.mouse.move(aliceBox.x + 180, aliceBox.y + 180);
  await bob.mouse.move(bobBox.x + 260, bobBox.y + 210);
  const cursors = (page, name) => page.locator(`[data-cursor-name="${name}"]`);
  await expect(observer.locator('[data-cursor-name]')).toHaveCount(2);
  await expect(cursors(observer, 'Alice')).toBeVisible();
  await expect(cursors(observer, 'Bob')).toBeVisible();
  const aliceColor = await cursors(bob, 'Alice').locator('path').getAttribute('fill');
  const bobColor = await cursors(alice, 'Bob').locator('path').getAttribute('fill');
  await expect(cursors(observer, 'Alice').locator('path')).toHaveAttribute('fill', aliceColor);
  await expect(cursors(observer, 'Bob').locator('path')).toHaveAttribute('fill', bobColor);
  expect(aliceColor).not.toBe(bobColor);
  const cursor = cursors(observer, 'Bob');
  const height = (await cursor.locator('text').boundingBox()).height;
  await observer.getByRole('button', { name: 'Zoom in', exact: true }).evaluate((el) => el.click());
  expect((await cursor.locator('text').boundingBox()).height).toBeCloseTo(height, 1);
  expect(await cursor.evaluate((el) => getComputedStyle(el).pointerEvents)).toBe('none');
  // Begin a drawing exactly where Bob's remote cursor is displayed.
  await draw(alice, 'rectangle', 260, 210, 140, 80);
  for (const page of [alice, bob, observer]) await expect(page.locator('[data-shape-type="rectangle"]')).toHaveCount(1);
  await expect(cursors(alice, 'Bob')).toBeVisible();
  await observer.close();
});

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

test('concurrent Ctrl+C / Ctrl+V pastes converge through CRDT and undo preserves another participant copy', async ({ whiteboard }) => {
  const { alice, bob } = whiteboard;
  const aliceGate = await updateGate(alice);
  const bobGate = await updateGate(bob);
  await whiteboard.join();
  await draw(alice, 'rectangle', 100, 160);
  const originalId = await alice.locator('[data-shape-id]').getAttribute('data-shape-id');
  await label(alice, originalId, 'API gateway');
  const baseline = await scene(alice);
  await expectScene([bob], baseline);
  const originalGeometry = await rectangle(alice, originalId);

  // Hold real Yjs updates until both participants paste against the same state.
  aliceGate.hold();
  bobGate.hold();
  await alice.keyboard.press('Control+c');
  await alice.keyboard.press('Control+v');
  await expect(alice.locator('[data-shape-id]')).toHaveCount(2);
  await expect(bob.locator('[data-shape-id]')).toHaveCount(1);
  const aliceCopyId = await alice.locator(`[data-shape-id]:not([data-shape-id="${originalId}"])`).getAttribute('data-shape-id');
  await select(bob, originalId);
  await bob.keyboard.press('Control+c');
  await bob.keyboard.press('Control+v');
  await expect(bob.locator('[data-shape-id]')).toHaveCount(2);
  await expect(alice.locator('[data-shape-id]')).toHaveCount(2);
  const bobCopyId = await bob.locator(`[data-shape-id]:not([data-shape-id="${originalId}"])`).getAttribute('data-shape-id');
  expect(new Set([originalId, aliceCopyId, bobCopyId]).size).toBe(3);
  for (const [page, id] of [[alice, aliceCopyId], [bob, bobCopyId]]) {
    await expect(shape(page, id)).toHaveText('API gateway');
    expect(await rectangle(page, id)).toEqual({
      ...originalGeometry, x: originalGeometry.x + 24, y: originalGeometry.y + 24
    });
  }
  await expect.poll(() => aliceGate.count).toBe(1);
  await expect.poll(() => bobGate.count).toBe(1);
  const merged = [...await scene(alice), ...(await scene(bob)).filter((item) => item.id === bobCopyId)]
    .sort((a, b) => a.id.localeCompare(b.id));
  bobGate.release();
  aliceGate.release();
  await expectScene([alice, bob], merged);
  const observer = await whiteboard.observe();
  await expectScene([observer], merged);

  // Local undo must retain the other participant's pasted element and later edits.
  await bob.bringToFront();
  await bob.locator('.board-canvas').focus();
  await bob.keyboard.press('Enter');
  const editor = bob.getByRole('textbox', { name: 'Element label' });
  await editor.fill('Edited by Bob');
  await editor.press('Enter');
  await expect(shape(bob, bobCopyId)).toHaveText('Edited by Bob');
  const edited = await scene(bob);
  await expectScene([alice, observer], edited);
  await alice.bringToFront();
  await alice.locator('.board-canvas').focus();
  await alice.keyboard.press('Control+z');
  await expectScene([alice, bob, observer], edited.filter((item) => item.id !== aliceCopyId));
  await alice.keyboard.press('Control+Shift+z');
  await expectScene([alice, bob, observer], edited);

  // A fresh Yjs session must restore the same IDs, labels and full SVG geometry.
  await join(bob, bob.url(), 'Bob after reload');
  await expectScene([bob], edited);
});

test('group drag, marquee, Ctrl-click and Cyrillic clipboard shortcuts synchronize across participants', async ({ whiteboard }) => {
  const { alice, bob } = whiteboard;
  await alice.context().grantPermissions(['clipboard-read', 'clipboard-write']);
  await whiteboard.join();
  for (const x of [100, 360, 620]) await draw(alice, 'rectangle', x, 170, 140, 90);
  const baseline = await scene(alice);
  await expectScene([bob], baseline);
  const ids = await alice.locator('[data-shape-id]').evaluateAll(elements => elements.map(el => el.getAttribute('data-shape-id')));
  const geometry = await Promise.all(ids.map(id => rectangle(alice, id)));
  const session = await alice.context().newCDPSession(alice);
  const shortcut = async (code, key, shift = false) => {
    await alice.bringToFront();
    await alice.locator('.board-canvas').focus();
    const event = { code, key, modifiers: shift ? 10 : 2, windowsVirtualKeyCode: code.charCodeAt(3) };
    await session.send('Input.dispatchKeyEvent', { ...event, type: 'keyDown' });
    await session.send('Input.dispatchKeyEvent', { ...event, type: 'keyUp' });
  };
  await shortcut('KeyA', 'ф');
  await expect(alice.locator('.board-selection')).toHaveCount(3);
  await expect(bob.locator('.board-selection')).toHaveCount(0);
  const box = await shape(alice, ids[0]).boundingBox();
  await drag(alice, box.x + box.width / 2, box.y + box.height / 2, 80, 50);
  for (const [i, id] of ids.entries()) expect(await rectangle(alice, id)).toEqual({ ...geometry[i], x: geometry[i].x + 80, y: geometry[i].y + 50 });
  const moved = await scene(alice);
  await expectScene([bob], moved);
  await shortcut('KeyZ', 'я'); await expectScene([alice, bob], baseline);
  await shortcut('KeyZ', 'Я', true); await expectScene([alice, bob], moved);
  const canvas = await alice.locator('.board-canvas').boundingBox();
  // Reverse drag on blank canvas encloses only the first two figures.
  await drag(alice, canvas.x + 600, canvas.y + 340, -440, -140);
  await expect(alice.locator('.board-selection')).toHaveCount(2);
  await shape(alice, ids[2]).click({ modifiers: ['Control'] });
  await expect(alice.locator('.board-selection')).toHaveCount(3);
  await shape(alice, ids[0]).click({ modifiers: ['Control'] });
  await expect(alice.locator('.board-selection')).toHaveCount(2);
  const subset = await shape(alice, ids[1]).boundingBox();
  await drag(alice, subset.x + subset.width / 2, subset.y + subset.height / 2, -30, 40);
  expect(await rectangle(alice, ids[0])).toEqual({ ...geometry[0], x: geometry[0].x + 80, y: geometry[0].y + 50 });
  for (const i of [1, 2]) expect(await rectangle(alice, ids[i])).toEqual({ ...geometry[i], x: geometry[i].x + 50, y: geometry[i].y + 90 });
  await expectScene([bob], await scene(alice));
  await shortcut('KeyC', 'с');
  await expect.poll(async () => JSON.parse(await alice.evaluate(() => navigator.clipboard.readText())).shapes.length).toBe(2);
  await shortcut('KeyV', 'м');
  await expect(alice.locator('[data-shape-id]')).toHaveCount(5);
  await expectScene([bob], await scene(alice));
  await shortcut('KeyZ', 'я'); await expect(alice.locator('[data-shape-id]')).toHaveCount(3);
  await expectScene([bob], await scene(alice));
  const observer = await whiteboard.observe();
  await expectScene([observer], await scene(alice));
});

test('wrapped labels, Ctrl+Enter and plain text paste preserve geometry and synchronize with undo', async ({ whiteboard }, testInfo) => {
  const { alice, bob } = whiteboard;
  await alice.context().grantPermissions(['clipboard-read', 'clipboard-write']);
  await whiteboard.join();
  await draw(alice, 'text', 100, 160, 110, 170);
  const id = await alice.locator('[data-shape-id]').getAttribute('data-shape-id');
  const editor = alice.getByRole('textbox', { name: 'Element label' });
  const unbroken = 'B'.repeat(45);
  await editor.fill(unbroken);
  await editor.press('End');
  await editor.press('Control+Enter');
  await editor.pressSequentially('Second line');
  await expect(editor).toHaveValue(`${unbroken}\nSecond line`);
  await editor.press('Enter');
  await expect(editor).toHaveCount(0);
  const rect = shape(alice, id).locator('rect.board-text-hit');
  await expect(rect).toHaveAttribute('width', '110');
  await expect(rect).toHaveAttribute('height', '170');
  const originalLines = await shape(alice, id).locator('tspan').count();
  expect(originalLines).toBeGreaterThan(2);
  const fits = async () => {
    const box = await rect.boundingBox();
    const label = await shape(alice, id).locator('.board-label').boundingBox();
    expect(label.x).toBeGreaterThanOrEqual(box.x - 1);
    expect(label.x + label.width).toBeLessThanOrEqual(box.x + box.width + 1);
    expect(label.y).toBeGreaterThanOrEqual(box.y - 1);
    expect(label.y + label.height).toBeLessThanOrEqual(box.y + box.height + 1);
  };
  await fits();
  await expectScene([bob], await scene(alice));
  await resize(alice, id, 110, 0);
  await expect(rect).toHaveAttribute('width', '220');
  expect(await shape(alice, id).locator('tspan').count()).toBeLessThan(originalLines);
  await fits();
  await expectScene([bob], await scene(alice));
  const baseline = await scene(alice);
  const plain = '<script>window.injected = 1</script>\nOrdinary clipboard text';
  await alice.evaluate(text => navigator.clipboard.writeText(text), plain);
  await alice.locator('.board-canvas').focus();
  await alice.keyboard.press('Control+v');
  await expect(alice.locator('[data-shape-type="text"]')).toHaveCount(2);
  const pasted = alice.locator(`[data-shape-type="text"]:not([data-shape-id="${id}"])`);
  const pastedId = await pasted.getAttribute('data-shape-id');
  await expect(pasted.locator('script')).toHaveCount(0);
  expect(await alice.evaluate(() => window.injected)).toBeUndefined();
  await alice.keyboard.press('Enter');
  await expect(editor).toHaveValue(plain);
  // Pasting while editing remains native text paste and creates no new shapes.
  await editor.press('End');
  await editor.press('Control+v');
  await expect(editor).toHaveValue(plain + plain);
  await editor.press('Escape');
  await expect(alice.locator('[data-shape-type="text"]')).toHaveCount(2);
  await expectScene([bob], await scene(alice));
  await alice.keyboard.press('Control+z');
  await expectScene([alice, bob], baseline);
  await alice.keyboard.press('Control+Shift+z');
  await expect(shape(alice, pastedId)).toHaveCount(1);
  await expectScene([bob], await scene(alice));
  await alice.screenshot({ path: testInfo.outputPath('wrapped-text-and-plain-paste.png') });
});

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
