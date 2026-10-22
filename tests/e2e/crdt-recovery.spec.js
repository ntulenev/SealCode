import { advance } from './advance.js';
import { csrf } from './admin-token.js';
import { expect, test } from './fixtures.js';

async function createRoom(request) {
  const response = await request.post('/admin/rooms', {
    headers: await csrf(request),
    data: { name: `Recovery ${Date.now()}`, language: 'csharp' }
  });
  expect(response.ok()).toBeTruthy();
  return (await response.json()).RoomId;
}

async function join(page, roomId, name) {
  await page.clock.install();
  await page.goto(`/room/${roomId}`);
  await page.locator('#displayNameInput').fill(name);
  await page.locator('#joinBtn').click();
  await expect(page.locator('#joinOverlay')).toHaveClass(/hidden/);
}

async function text(page) {
  return page.evaluate(() => monaco.editor.getModels()[0].getValue());
}

async function insert(page, value) {
  await page.evaluate(
    (value) =>
      monaco.editor
        .getModels()[0]
        .applyEdits([{ range: new monaco.Range(1, 1, 1, 1), text: value }]),
    value
  );
}

for (const scenario of ['gap', 'last-update']) {
  test(`recovers a missed ${scenario} without reconnecting or losing undo`, async ({
    browser,
    request
  }) => {
    const roomId = await createRoom(request);
    const context = await browser.newContext();
    const alice = await context.newPage();
    const bob = await context.newPage();
    let dropped = 0;
    await alice.routeWebSocket('**/roomHub*', (route) => {
      const server = route.connectToServer();
      server.onMessage((message) => {
        if (typeof message !== 'string') return route.send(message);
        const records = message
          .split('\x1e')
          .filter(Boolean)
          .filter((record) => {
            const frame = JSON.parse(record);
            if (frame.target === 'YjsUpdated' && frame.arguments[1] === 1) {
              dropped++;
              return false;
            }
            return true;
          });
        if (records.length) route.send(records.join('\x1e') + '\x1e');
      });
    });
    await join(alice, roomId, 'Alice');
    await join(bob, roomId, 'Bob');
    await insert(bob, 'B');
    await expect
      .poll(() => bob.evaluate(() => window.__debugUndoState().documentRevision))
      .toBe(1);
    if (scenario === 'gap') await insert(alice, 'A');
    await expect
      .poll(async () => (await text(alice)).split('').sort().join(''))
      .toBe(scenario === 'gap' ? 'AB' : 'B');
    expect(await text(alice)).toBe(await text(bob));
    expect(dropped).toBe(1);
    if (scenario === 'gap') {
      await alice.locator('#monacoEditor').click();
      await alice.keyboard.press('Control+z');
      await expect.poll(() => text(alice)).toBe('B');
      await expect.poll(() => text(bob)).toBe('B');
    }
    await context.close();
  });
}

for (const notification of ['received', 'missed']) {
  test(`stops in-flight retries when room closure notification is ${notification}`, async ({
    browser,
    request
  }) => {
    const roomId = await createRoom(request);
    const context = await browser.newContext();
    const page = await context.newPage();
    let releaseUpdate;
    let updateAttempts = 0;
    await page.routeWebSocket('**/roomHub*', (route) => {
      const server = route.connectToServer();
      route.onMessage((message) => {
        const frames =
          typeof message === 'string'
            ? message
                .split('\x1e')
                .filter(Boolean)
                .map((record) => JSON.parse(record))
            : [];
        if (frames.some((frame) => frame.target === 'UpdateYjs')) {
          updateAttempts++;
          if (!releaseUpdate) {
            releaseUpdate = () => server.send(message);
            return;
          }
        }
        server.send(message);
      });
      server.onMessage((message) => {
        if (notification !== 'missed' || typeof message !== 'string')
          return route.send(message);
        const records = message
          .split('\x1e')
          .filter(Boolean)
          .filter((record) => JSON.parse(record).target !== 'RoomKilled');
        if (records.length) route.send(records.join('\x1e') + '\x1e');
      });
    });
    await join(page, roomId, 'Alice');
    await insert(page, 'Keep this text');
    await expect.poll(() => !!releaseUpdate).toBe(true);
    expect(
      (
        await request.delete(`/admin/rooms/${roomId}`, { headers: await csrf(request) })
      ).ok()
    ).toBeTruthy();
    if (notification === 'received') {
      await expect(page.locator('#joinOverlay')).not.toHaveClass(/hidden/);
    }
    releaseUpdate();
    await expect(page.locator('#joinOverlay')).not.toHaveClass(/hidden/);
    await advance(page, 1200);
    expect(updateAttempts).toBe(1);
    expect(await text(page)).toBe('Keep this text');
    expect(await page.evaluate(() => window.__debugYjsSend())).toBe(false);
    await context.close();
  });
}

test('language events cannot roll back metadata and a missed final event is recovered', async ({
  browser,
  request
}) => {
  const roomId = await createRoom(request);
  const context = await browser.newContext();
  const alice = await context.newPage();
  const bob = await context.newPage();
  let held;
  let seen = 0;
  await alice.routeWebSocket('**/roomHub*', (route) => {
    const server = route.connectToServer();
    server.onMessage((message) => {
      if (typeof message !== 'string') return route.send(message);
      for (const record of message.split('\x1e').filter(Boolean)) {
        const frame = JSON.parse(record);
        if (frame.target === 'LanguageUpdated') {
          seen++;
          if (seen === 1) {
            held = record;
            continue;
          }
          if (seen === 3) continue;
          route.send(record + '\x1e');
          if (held) {
            route.send(held + '\x1e');
            held = null;
          }
        } else route.send(record + '\x1e');
      }
    });
  });
  await join(alice, roomId, 'Alice');
  await join(bob, roomId, 'Bob');
  await bob.locator('#languageSelect').selectOption('sql');
  await expect.poll(() => seen).toBe(1);
  await insert(bob, 'text raises room version independently');
  await bob.locator('#languageSelect').selectOption('csharp');
  await expect.poll(() => seen).toBe(2);
  await expect(alice.locator('#languageSelect')).toHaveValue('csharp');
  await bob.locator('#languageSelect').selectOption('sql');
  await expect.poll(() => seen).toBe(3);
  await expect(alice.locator('#languageSelect')).toHaveValue('sql', { timeout: 10000 });
  expect(await text(alice)).toBe(await text(bob));
  await context.close();
});

test('recovers missed edits after offline interrupts a revision probe', async ({
  browser,
  request
}) => {
  const roomId = await createRoom(request);
  const context = await browser.newContext();
  const alice = await context.newPage();
  const bob = await context.newPage();
  let held;
  let probeId;
  let joins = 0;
  let rejoinId;
  let releaseJoin;
  let dropped = 0;
  let dropUpdates = false;
  await alice.routeWebSocket('**/roomHub*', (route) => {
    const server = route.connectToServer();
    route.onMessage((message) => {
      if (typeof message === 'string') {
        for (const record of message.split('\x1e').filter(Boolean)) {
          const frame = JSON.parse(record);
          if (frame.target === 'JoinRoom' && ++joins > 1) rejoinId = frame.invocationId;
          if (frame.target === 'GetRoomRevisions' && probeId === undefined) {
            probeId = frame.invocationId;
          }
        }
      }
      server.send(message);
    });
    server.onMessage((message) => {
      if (typeof message !== 'string') return route.send(message);
      for (const record of message.split('\x1e').filter(Boolean)) {
        const frame = JSON.parse(record);
        if (frame.type === 3 && frame.invocationId === probeId && !held) {
          held = () => route.send(record + '\x1e');
        } else if (frame.type === 3 && frame.invocationId === rejoinId) {
          releaseJoin = () => route.send(record + '\x1e');
        } else if (dropUpdates && frame.target === 'YjsUpdated') {
          dropped++;
        } else {
          route.send(record + '\x1e');
        }
      }
    });
  });
  try {
    await join(alice, roomId, 'Alice');
    await join(bob, roomId, 'Bob');
    await alice.clock.runFor(5500);
    await expect.poll(() => !!held).toBe(true);
    await context.setOffline(true);
    await context.setOffline(false);
    await expect.poll(() => !!releaseJoin).toBe(true);
    held();
    await alice.waitForTimeout(50);
    releaseJoin();
    await expect(alice.locator('#connectionStatus')).toHaveText('connected');
    dropUpdates = true;
    await insert(bob, 'Recovered');
    await expect.poll(() => text(bob)).toBe('Recovered');
    await expect
      .poll(async () => {
        await bob.clock.runFor(200);
        return bob.evaluate(() => window.__debugUndoState().documentRevision);
      })
      .toBe(1);
    await expect.poll(() => dropped).toBe(1);
    expect(await text(alice)).toBe('');
    dropUpdates = false;
    await expect
      .poll(async () => {
        await alice.clock.runFor(1000);
        return text(alice);
      })
      .toBe('Recovered');
  } finally {
    await context.close();
  }
});
