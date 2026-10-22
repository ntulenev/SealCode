import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  RoomLifecycle,
  registerHubHandlers
} from '../../src/SealCode/wwwroot/room-lifecycle.js';

function create(
  invoke = async () => {
    throw new Error('offline');
  }
) {
  const jobs = new Map();
  const delays = [];
  const callbacks = {};
  let id = 0;
  const connection = {
    state: 'Connected',
    invoke,
    on: (name, callback) => {
      callbacks[name] = callback;
    }
  };
  const lifecycle = new RoomLifecycle({
    connection,
    roomId: 'room',
    getDisplayName: () => 'Alice',
    session: { synchronizationEpoch: 0, bufferedYjsMessages: [] },
    start: async () => true,
    onJoinStart() {},
    applyJoin() {},
    onJoinSuccess() {},
    onJoinError() {},
    onSynchronizationEnd() {},
    onClose() {},
    setStatus() {},
    setReadOnly() {},
    resetTransport() {},
    setTimer: (fn, delay) => {
      delays.push(delay);
      jobs.set(++id, fn);
      return id;
    },
    clearTimer: (timer) => jobs.delete(timer)
  });
  lifecycle.joined = true;
  return { lifecycle, jobs, delays, callbacks, connection };
}

test('rejoin backs off through every configured delay and caps at the last', async () => {
  const { lifecycle, jobs, delays } = create();
  await lifecycle.rejoinRoomWithRetry();
  for (let i = 0; i < 6; i++) {
    const [key, run] = jobs.entries().next().value;
    jobs.delete(key);
    await run();
  }
  assert.deepEqual(delays, [500, 1000, 2000, 5000, 10000, 10000, 10000]);
  lifecycle.closeRoom('Room expired');
  assert.equal(jobs.size, 0);
  await lifecycle.rejoinRoomWithRetry();
  assert.equal(jobs.size, 0);
});

test('RoomKilled during rejoin prevents a late reply from reopening the room', async () => {
  let resolve;
  const result = create(
    () =>
      new Promise((done) => {
        resolve = done;
      })
  );
  const { lifecycle, connection, callbacks, jobs } = result;
  registerHubHandlers(connection, {
    RoomKilled: (reason) => lifecycle.closeRoom(reason)
  });
  const pending = lifecycle.rejoinRoomWithRetry();
  callbacks.RoomKilled('Room expired');
  resolve({});
  await pending;
  assert.equal(lifecycle.closed, true);
  assert.equal(lifecycle.joined, false);
  assert.equal(jobs.size, 0);
});

test('online, offline and connection callbacks coordinate rejoin and read-only state', async (context) => {
  let joins = 0;
  const { lifecycle, connection } = create(async () => {
    joins += 1;
    return {};
  });
  const network = new Map();
  const statuses = [];
  const readOnly = [];
  let resets = 0;
  let reconnected;
  let reconnecting;
  let closed;
  connection.onreconnected = (handler) => {
    reconnected = handler;
  };
  connection.onreconnecting = (handler) => {
    reconnecting = handler;
  };
  connection.onclose = (handler) => {
    closed = handler;
  };
  lifecycle.setStatus = (status) => statuses.push(status);
  lifecycle.setReadOnly = (value) => readOnly.push(value);
  lifecycle.resetTransport = () => {
    resets += 1;
  };
  lifecycle.start = async () => {
    connection.state = 'Connected';
    return true;
  };
  lifecycle.registerConnectivity({
    addEventListener: (name, handler) => network.set(name, handler)
  });
  network.get('offline')();
  assert.equal(readOnly.at(-1), true);
  connection.state = 'Disconnected';
  network.get('online')();
  await new Promise(setImmediate);
  assert.equal(joins, 1);
  assert.equal(statuses.at(-1), 'connected');
  assert.equal(readOnly.at(-1), false);
  network.get('online')();
  assert.equal(joins, 1);
  reconnecting();
  closed();
  assert.equal(resets, 4);
  assert.equal(statuses.at(-1), 'disconnected');
  await reconnected();
  assert.equal(joins, 2);
  const warning = context.mock.method(console, 'warn', () => {});
  lifecycle.start = async () => {
    throw new Error('network failed');
  };
  connection.state = 'Disconnected';
  network.get('online')();
  await new Promise(setImmediate);
  assert.equal(warning.mock.callCount(), 1);
  lifecycle.closeRoom('closed');
  network.get('online')();
  network.get('offline')();
  await reconnected();
  assert.equal(joins, 2);
});

test('a missing room ends rejoin without scheduling another attempt', async () => {
  const { lifecycle, jobs } = create(async () => {
    throw new Error('HubException: Room not found');
  });
  let reason;
  lifecycle.onClose = (value) => {
    reason = value;
  };
  await lifecycle.rejoinRoomWithRetry();
  assert.equal(lifecycle.closed, true);
  assert.equal(reason, 'Room not found.');
  assert.equal(jobs.size, 0);
});

test('closing a room stops its transport exactly once', async () => {
  const { lifecycle } = create();
  let stops = 0;
  lifecycle.stop = async () => {
    stops++;
  };
  lifecycle.closeRoom('closed');
  lifecycle.closeRoom('closed again');
  await new Promise(setImmediate);
  assert.equal(stops, 1);
});

test('participation is restored only after a successful rejoin', async () => {
  const { lifecycle, connection, jobs } = create();
  let initialErrors = 0;
  let rejoinErrors = 0;
  lifecycle.onJoinError = () => {
    initialErrors++;
  };
  lifecycle.onRejoinError = () => {
    rejoinErrors++;
  };
  lifecycle.participating = true;
  lifecycle.pause('reconnecting');
  assert.equal(lifecycle.participating, false);
  await lifecycle.rejoinRoomWithRetry();
  assert.equal(initialErrors, 0);
  assert.equal(rejoinErrors, 1);
  assert.equal(jobs.size, 1);
  connection.invoke = async () => ({});
  await lifecycle.rejoinRoomWithRetry();
  assert.equal(lifecycle.participating, true);
});

test('online with a live transport rejoins before enabling editing', async () => {
  const { lifecycle, connection } = create();
  const events = new Map();
  const readOnly = [];
  connection.onreconnected = connection.onreconnecting = connection.onclose = () => {};
  lifecycle.setReadOnly = (value) => readOnly.push(value);
  lifecycle.registerConnectivity({
    addEventListener: (name, fn) => events.set(name, fn)
  });
  events.get('online')();
  await new Promise(setImmediate);
  assert.equal(lifecycle.participating, false);
  assert.ok(!readOnly.includes(false));
  connection.invoke = async () => ({});
  events.get('online')();
  await new Promise(setImmediate);
  assert.equal(lifecycle.participating, true);
  assert.equal(readOnly.at(-1), false);
  lifecycle.closeRoom('done');
});

test('offline resets once and online resets before joining on the live connection', async () => {
  const calls = [];
  const { lifecycle, connection } = create(async () => {
    calls.push('join');
    return {};
  });
  const events = new Map();
  connection.onreconnected = connection.onreconnecting = connection.onclose = () => {};
  lifecycle.resetTransport = () => calls.push('reset');
  lifecycle.registerConnectivity({
    addEventListener: (name, fn) => events.set(name, fn)
  });
  events.get('offline')();
  assert.deepEqual(calls, ['reset']);
  events.get('online')();
  await new Promise(setImmediate);
  assert.deepEqual(calls, ['reset', 'reset', 'join']);
});

test('a stale rejoin cannot unlock or schedule over a newer rejoin', async () => {
  const replies = [];
  const { lifecycle } = create(() => new Promise((resolve) => replies.push(resolve)));
  const old = lifecycle.rejoinRoomWithRetry();
  lifecycle.pause('reconnecting');
  const current = lifecycle.rejoinRoomWithRetry();
  replies[0]({});
  await old;
  assert.equal(lifecycle.rejoining, true);
  await lifecycle.rejoinRoomWithRetry();
  assert.equal(replies.length, 2);
  replies[1]({});
  await current;
  assert.equal(lifecycle.rejoining, false);
});
