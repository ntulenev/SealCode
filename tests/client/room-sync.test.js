import { RevisionedRoomView } from '../../src/SealCode/wwwroot/room-presence.js';
import test from 'node:test';
import assert from 'node:assert/strict';
import { UpdateQueue, SyncScheduler, SnapshotRecovery, CheckpointWriter } from '../../src/SealCode/wwwroot/room-sync.js';

function fixture(send, budgets = {}) {
  let id = 0; const timers = new Map(); const errors = [];
  const queue = new UpdateQueue({ send, merge: arrays => Uint8Array.from(arrays.flatMap(array => [...array])), ready: () => true,
    onIdle() {}, onSuccess() {}, onError: (error, permanent) => errors.push(permanent),
    setTimer: (fn, delay) => { timers.set(++id, { fn, delay }); return id; }, clearTimer: id => timers.delete(id), ...budgets });
  return { queue, timers, errors, tick: async () => { const [key, timer] = timers.entries().next().value; timers.delete(key); await timer.fn(); } };
}
test('permanent refusal retains updates and stops retries', async () => {
  const f = fixture(() => { throw new Error('LimitExceeded'); });
  f.queue.enqueue(Uint8Array.of(1)); await f.tick();
  assert.equal(f.queue.pending.length, 1); assert.equal(f.queue.blocked, true);
  assert.equal(f.timers.size, 0); assert.deepEqual(f.errors, [true]);
});
test('rate limit retains updates and backs off before successful retry', async () => {
  let calls = 0;
  const f = fixture(() => { if (++calls === 1) throw new Error('RateLimited'); });
  f.queue.enqueue(Uint8Array.of(1)); await f.tick();
  assert.equal([...f.timers.values()][0].delay, 1100);
  await f.tick(); assert.equal(f.queue.pending.length, 0); assert.equal(calls, 2);
});
test('queue counts in-flight bytes against its budget', async () => {
  let release;
  const f = fixture(() => new Promise(resolve => { release = resolve; }), { maxBytes: 2 });
  f.queue.enqueue(Uint8Array.of(1, 2)); const flight = f.tick();
  f.queue.enqueue(Uint8Array.of(3)); assert.equal(f.queue.blocked, true);
  release(); await flight; assert.equal(f.queue.pending.length, 0);
});
test('session reset ignores late failures and releases queued buffers', async () => {
  let reject;
  const f = fixture(() => new Promise((_, fail) => { reject = fail; }));
  f.queue.enqueue(Uint8Array.of(1)); const flight = f.tick(); f.queue.reset();
  reject(new Error('temporary')); await flight;
  assert.equal(f.queue.pending.length, 0); assert.equal(f.queue.inFlight, false); assert.equal(f.errors.length, 0);
});
test('scheduler keeps earlier recovery and cancels every session timer', () => {
  let id = 0; let now = 10; const timers = new Map();
  const scheduler = new SyncScheduler({ now: () => now, setTimer: (fn, delay) => { timers.set(++id, { fn, delay }); return id; }, clearTimer: id => timers.delete(id) });
  scheduler.schedule('recovery', 5000, () => {}); now = 20;
  scheduler.schedule('recovery', 250, () => {}); scheduler.schedule('recovery', 5000, () => {});
  assert.equal(timers.size, 1); assert.equal([...timers.values()][0].delay, 250);
  scheduler.schedule('checkpoint', 400, () => {}); scheduler.reset(); assert.equal(timers.size, 0);
});

test('metadata-only gap recovers and ends synchronization in order', async () => {
  const calls = [];
  const recovery = new SnapshotRecovery({ scheduler: { schedule() {}, cancel() {} }, active: () => true, ready: () => true,
    capture: () => 1, current: () => true,
    revisions: () => ({ DocumentRevision: 5, MetadataRevision: 1, PresenceRevision: 1 }),
    probe: async () => ({ DocumentRevision: 5, MetadataRevision: 2, PresenceRevision: 1 }),
    snapshot: async () => { calls.push('snapshot'); return {}; }, begin: () => { calls.push('begin'); return 9; },
    apply: () => calls.push('merge'), end: token => calls.push(`end:${token}`), onSuccess: () => calls.push('success'), onError: error => { throw error; }
  });
  await recovery.run();
  assert.deepEqual(calls, ['begin', 'snapshot', 'merge', 'success', 'end:9']);
  assert.equal(recovery.inFlight, false);
});
test('snapshot arriving after closure never merges into a later session', async () => {
  let active = true; let release; let merged = false;
  const recovery = new SnapshotRecovery({ scheduler: { schedule() {}, cancel() {} }, active: () => active, ready: () => true,
    capture: () => 1, current: () => active, revisions: () => ({ DocumentRevision: 0, MetadataRevision: 0, PresenceRevision: 0 }),
    probe: async () => ({ DocumentRevision: 1, MetadataRevision: 0, PresenceRevision: 0 }),
    snapshot: () => new Promise(resolve => { release = resolve; }), begin: () => 1,
    apply: () => { merged = true; }, end() {}, onSuccess() {}, onError() {}
  });
  const flight = recovery.run(); await Promise.resolve(); active = false; recovery.reset(); release({}); await flight;
  assert.equal(merged, false); assert.equal(recovery.inFlight, false);
});
test('oversized checkpoint is skipped until the document revision changes', async () => {
  let revision = 1; let scheduled = 0; let attempts = 0;
  const writer = new CheckpointWriter({ scheduler: { schedule() { scheduled++; }, cancel() {} }, ready: () => true,
    capture: () => 1, current: () => true, revision: () => revision,
    payload: () => { attempts++; throw new Error('LimitExceeded'); }, send() {}, onSuccess() {}, onError() {}
  });
  await writer.run(); writer.schedule();
  assert.equal(attempts, 1); assert.equal(scheduled, 0);
  revision++; writer.schedule(); assert.equal(scheduled, 1);
});

test('independent metadata revisions reject reversed language and presence events', () => {
  const languages = []; const users = [];
  const view = new RevisionedRoomView({ language: value => languages.push(value), users: value => users.push(value) });
  view.updateLanguage('sql', 2); view.updateLanguage('csharp', 1);
  view.updateUsers(['Alice'], 4); view.updateUsers(['Alice', 'Bob'], 3);
  assert.deepEqual(languages, ['sql']); assert.deepEqual(users, [['Alice']]);
  view.apply({ Language: 'javascript', MetadataRevision: 3, Users: [], PresenceRevision: 5 });
  assert.equal(languages.at(-1), 'javascript'); assert.deepEqual(users.at(-1), []);
});
