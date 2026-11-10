import { test } from 'node:test';
import assert from 'node:assert/strict';
import { WhiteboardPresence } from '../../src/SealCode/wwwroot/whiteboard-presence.js';

function fixture(t, send) {
  t.mock.timers.enable({ apis: ['setTimeout', 'setInterval', 'Date'], now: 10000 });
  const cursors = new Map();
  const sent = [];
  const handlers = {};
  const board = {
    setRemoteCursor: (name, point) => cursors.set(name, point),
    removeRemoteCursor: (name) => cursors.delete(name),
    clearRemoteCursors: () => cursors.clear()
  };
  const document = { hidden: false, addEventListener: (name, fn) => { handlers[name] = fn; }, defaultView: { addEventListener: (name, fn) => { handlers[name] = fn; } } };
  let ready = true;
  const presence = new WhiteboardPresence({ board, document, ready: () => ready,
    send: send ?? (async (point) => { sent.push(point); }) });
  presence.retainUsers(['Alice', 'Bob']);
  presence.start();
  t.after(() => presence.reset());
  return { presence, board, cursors, sent, document, handlers, offline: () => { ready = false; } };
}

test('cursor bursts send the latest position, throttle and keep only one invocation in flight', async (t) => {
  const sent = [];
  let finish;
  const f = fixture(t, (point) => { sent.push(point); return new Promise((resolve) => { finish = resolve; }); });
  f.board.onPointer({ x: 1, y: 2 });
  f.board.onPointer({ x: 3, y: 4 });
  t.mock.timers.tick(1);
  assert.deepEqual(sent, [{ x: 3, y: 4 }]);
  for (let x = 0; x < 100; x++) f.board.onPointer({ x, y: -5 });
  t.mock.timers.tick(100);
  assert.equal(sent.length, 1);
  finish();
  await Promise.resolve();
  t.mock.timers.tick(1);
  assert.deepEqual(sent.at(-1), { x: 99, y: -5 });
  finish();
  await Promise.resolve();
  f.board.onPointer({ x: 101, y: 0 });
  t.mock.timers.tick(30);
  assert.equal(sent.length, 2);
  t.mock.timers.tick(30);
  assert.equal(sent.length, 3);
  finish();
});

test('heartbeats keep stationary cursors alive and stale peers expire', async (t) => {
  const f = fixture(t);
  f.board.onPointer({ x: -100, y: 200 });
  t.mock.timers.tick(1);
  await Promise.resolve();
  f.presence.receive('Bob', -100, 200);
  f.presence.receive('Unknown', 0, 0);
  f.presence.receive('Alice', NaN, 0);
  assert.equal(f.cursors.size, 1);
  t.mock.timers.tick(6000);
  t.mock.timers.tick(1);
  await Promise.resolve();
  assert.equal(f.sent.length, 2);
  assert.deepEqual(f.sent[0], f.sent[1]);
  t.mock.timers.tick(10000);
  assert.equal(f.cursors.size, 0);
});

test('leaving, hiding, disconnecting and reconnecting discard ephemeral state', async (t) => {
  const f = fixture(t);
  f.presence.receive('Bob', 1, 2);
  f.presence.receive('Bob', null, null);
  assert.equal(f.cursors.size, 0);
  f.presence.receive('Bob', 1, 2);
  f.presence.retainUsers(['Alice']);
  assert.equal(f.cursors.size, 0);
  f.board.onPointer({ x: 2, y: 3 });
  t.mock.timers.tick(1);
  await Promise.resolve();
  f.document.hidden = true;
  f.handlers.visibilitychange();
  t.mock.timers.tick(60);
  await Promise.resolve();
  assert.equal(f.sent.at(-1), null);
  f.document.hidden = false;
  f.board.onPointer({ x: 4, y: 5 });
  f.handlers.blur();
  t.mock.timers.tick(60);
  await Promise.resolve();
  assert.equal(f.sent.at(-1), null);
  f.board.onPointer({ x: 6, y: 7 });
  f.presence.reset();
  f.presence.start();
  t.mock.timers.tick(6000);
  assert.equal(f.sent.at(-1), null);
  f.offline();
  f.board.onPointer({ x: 8, y: 9 });
  f.presence.receive('Alice', 1, 1);
  assert.equal(f.cursors.size, 0);
});

test('failed presence sends do not block later positions', async (t) => {
  const f = fixture(t, async () => { throw new Error('offline'); });
  f.board.onPointer({ x: 0, y: 0 });
  t.mock.timers.tick(1);
  await Promise.resolve();
  assert.equal(f.presence.inFlight, false);
  f.board.onPointer({ x: 1, y: 1 });
  t.mock.timers.tick(60);
  await Promise.resolve();
  assert.equal(f.presence.inFlight, false);
});

test('a late completion from the previous connection cannot release a new in-flight send', async (t) => {
  const pending = [];
  const sent = [];
  const f = fixture(t, (point) => {
    sent.push(point);
    return new Promise((resolve) => pending.push(resolve));
  });
  f.board.onPointer({ x: 1, y: 1 });
  t.mock.timers.tick(1);
  f.presence.reset();
  f.presence.start();
  f.board.onPointer({ x: 2, y: 2 });
  t.mock.timers.tick(60);
  f.board.onPointer({ x: 3, y: 3 });
  pending[0]();
  await Promise.resolve();
  t.mock.timers.tick(60);
  assert.equal(sent.length, 2);
  pending[1]();
  await Promise.resolve();
  t.mock.timers.tick(1);
  assert.deepEqual(sent.at(-1), { x: 3, y: 3 });
  pending[2]();
  await Promise.resolve();
});

test('fresh peer updates renew expiry and unchanged local coordinates do not send again', async (t) => {
  const f = fixture(t);
  f.board.onPointer({ x: 1, y: 2 });
  t.mock.timers.tick(1);
  await Promise.resolve();
  f.board.onPointer({ x: 1, y: 2 });
  t.mock.timers.tick(100);
  assert.equal(f.sent.length, 1);
  f.presence.receive('Bob', 1, 2);
  t.mock.timers.tick(14000);
  f.presence.receive('Bob', 3, 4);
  t.mock.timers.tick(2000);
  assert.deepEqual(f.cursors.get('Bob'), { x: 3, y: 4 });
  f.presence.receive('Bob', null, 0);
  f.presence.receive('Bob', Infinity, 0);
  assert.deepEqual(f.cursors.get('Bob'), { x: 3, y: 4 });
  t.mock.timers.tick(14000);
  assert.equal(f.cursors.size, 0);
});
