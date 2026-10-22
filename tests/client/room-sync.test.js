import { RevisionedRoomView } from '../../src/SealCode/wwwroot/room-presence.js';
import test from 'node:test';
import assert from 'node:assert/strict';
import {
  canSendUpdates,
  canRecover,
  canWriteCheckpoint,
  UpdateQueue,
  SyncScheduler,
  SnapshotRecovery,
  shouldWriteCheckpoint,
  CheckpointWriter,
  CheckpointJournalTracker
} from '../../src/SealCode/wwwroot/room-sync.js';

function fixture(send, budgets = {}) {
  let id = 0;
  const timers = new Map();
  const errors = [];
  const queue = new UpdateQueue({
    send,
    merge: (arrays) => Uint8Array.from(arrays.flatMap((array) => [...array])),
    ready: () => true,
    onIdle() {},
    onSuccess() {},
    onError: (error, permanent) => errors.push(permanent),
    setTimer: (fn, delay) => {
      timers.set(++id, { fn, delay });
      return id;
    },
    clearTimer: (id) => timers.delete(id),
    ...budgets
  });
  return {
    queue,
    timers,
    errors,
    tick: async () => {
      const [key, timer] = timers.entries().next().value;
      timers.delete(key);
      await timer.fn();
    }
  };
}
test('permanent refusal retains updates and stops retries', async () => {
  const f = fixture(() => {
    throw new Error('LimitExceeded');
  });
  f.queue.enqueue(Uint8Array.of(1));
  await f.tick();
  assert.equal(f.queue.pending.length, 1);
  assert.equal(f.queue.blocked, true);
  assert.equal(f.timers.size, 0);
  assert.deepEqual(f.errors, [true]);
});
test('rate limit retains updates and backs off before successful retry', async () => {
  let calls = 0;
  const f = fixture(() => {
    if (++calls === 1) throw new Error('RateLimited');
  });
  f.queue.enqueue(Uint8Array.of(1));
  await f.tick();
  assert.equal([...f.timers.values()][0].delay, 1100);
  await f.tick();
  assert.equal(f.queue.pending.length, 0);
  assert.equal(calls, 2);
});
test('queue counts in-flight bytes against its budget', async () => {
  let release;
  const f = fixture(
    () =>
      new Promise((resolve) => {
        release = resolve;
      }),
    { maxBytes: 2 }
  );
  f.queue.enqueue(Uint8Array.of(1, 2));
  const flight = f.tick();
  f.queue.enqueue(Uint8Array.of(3));
  assert.equal(f.queue.blocked, true);
  release();
  await flight;
  assert.equal(f.queue.pending.length, 0);
});
test('session reset ignores late failures and releases queued buffers', async () => {
  let reject;
  const f = fixture(
    () =>
      new Promise((_, fail) => {
        reject = fail;
      })
  );
  f.queue.enqueue(Uint8Array.of(1));
  const flight = f.tick();
  f.queue.reset();
  reject(new Error('temporary'));
  await flight;
  assert.equal(f.queue.pending.length, 0);
  assert.equal(f.queue.inFlight, false);
  assert.equal(f.errors.length, 0);
});
test('scheduler keeps earlier recovery and cancels every session timer', () => {
  let id = 0;
  let now = 10;
  const timers = new Map();
  const scheduler = new SyncScheduler({
    now: () => now,
    setTimer: (fn, delay) => {
      timers.set(++id, { fn, delay });
      return id;
    },
    clearTimer: (id) => timers.delete(id)
  });
  scheduler.schedule('recovery', 5000, () => {});
  now = 20;
  scheduler.schedule('recovery', 250, () => {});
  scheduler.schedule('recovery', 5000, () => {});
  assert.equal(timers.size, 1);
  assert.equal([...timers.values()][0].delay, 250);
  scheduler.schedule('checkpoint', 400, () => {});
  scheduler.reset();
  assert.equal(timers.size, 0);
});

test('metadata-only gap recovers and ends synchronization in order', async () => {
  const calls = [];
  const recovery = new SnapshotRecovery({
    scheduler: { schedule() {}, cancel() {} },
    active: () => true,
    ready: () => true,
    capture: () => 1,
    current: () => true,
    revisions: () => ({ DocumentRevision: 5, MetadataRevision: 1, PresenceRevision: 1 }),
    probe: async () => ({
      DocumentRevision: 5,
      MetadataRevision: 2,
      PresenceRevision: 1
    }),
    snapshot: async () => {
      calls.push('snapshot');
      return {};
    },
    begin: () => {
      calls.push('begin');
      return 9;
    },
    apply: () => calls.push('merge'),
    end: (token) => calls.push(`end:${token}`),
    onSuccess: () => calls.push('success'),
    onError: (error) => {
      throw error;
    }
  });
  await recovery.run();
  assert.deepEqual(calls, ['begin', 'snapshot', 'merge', 'success', 'end:9']);
  assert.equal(recovery.inFlight, false);
});
test('snapshot arriving after closure never merges into a later session', async () => {
  let active = true;
  let release;
  let merged = false;
  const recovery = new SnapshotRecovery({
    scheduler: { schedule() {}, cancel() {} },
    active: () => active,
    ready: () => true,
    capture: () => 1,
    current: () => active,
    revisions: () => ({ DocumentRevision: 0, MetadataRevision: 0, PresenceRevision: 0 }),
    probe: async () => ({
      DocumentRevision: 1,
      MetadataRevision: 0,
      PresenceRevision: 0
    }),
    snapshot: () =>
      new Promise((resolve) => {
        release = resolve;
      }),
    begin: () => 1,
    apply: () => {
      merged = true;
    },
    end() {},
    onSuccess() {},
    onError() {}
  });
  const flight = recovery.run();
  await Promise.resolve();
  active = false;
  recovery.reset();
  release({});
  await flight;
  assert.equal(merged, false);
  assert.equal(recovery.inFlight, false);
});
test('oversized checkpoint is skipped until the document revision changes', async () => {
  let revision = 1;
  let scheduled = 0;
  let attempts = 0;
  const writer = new CheckpointWriter({
    scheduler: {
      schedule() {
        scheduled++;
      },
      cancel() {}
    },
    ready: () => true,
    capture: () => 1,
    current: () => true,
    revision: () => revision,
    payload: () => {
      attempts++;
      throw new Error('LimitExceeded');
    },
    send() {},
    onSuccess() {},
    onError() {}
  });
  await writer.run();
  writer.schedule();
  assert.equal(attempts, 1);
  assert.equal(scheduled, 0);
  revision += 64;
  writer.schedule();
  assert.equal(scheduled, 1);
});

test('independent metadata revisions reject reversed language and presence events', () => {
  const languages = [];
  const users = [];
  const view = new RevisionedRoomView({
    language: (value) => languages.push(value),
    users: (value) => users.push(value)
  });
  view.updateLanguage('sql', 2);
  view.updateLanguage('csharp', 1);
  view.updateUsers(['Alice'], 4);
  view.updateUsers(['Alice', 'Bob'], 3);
  assert.deepEqual(languages, ['sql']);
  assert.deepEqual(users, [['Alice']]);
  view.apply({
    Language: 'javascript',
    MetadataRevision: 3,
    Users: [],
    PresenceRevision: 5
  });
  assert.equal(languages.at(-1), 'javascript');
  assert.deepEqual(users.at(-1), []);
});

for (const [label, revisions, bytes, author, expected] of [
  ['below thresholds', 63, 0, true, false],
  ['author revision threshold', 64, 0, true, true],
  ['author byte threshold', 1, 256 * 1024, true, true],
  ['observer at author threshold', 64, 256 * 1024, false, false],
  ['observer revision fallback', 128, 0, false, true],
  ['observer byte fallback', 1, 512 * 1024, false, true],
  ['same revision', 0, 512 * 1024, true, false],
  ['older revision', -1, 512 * 1024, true, false]
]) {
  test(`checkpoint policy: ${label}`, () => {
    assert.equal(
      shouldWriteCheckpoint({
        documentRevision: 10 + revisions,
        checkpointRevision: 10,
        bytesSinceCheckpoint: bytes,
        isLatestAuthor: author
      }),
      expected
    );
  });
}

test('checkpoint writer uses its configured delay', () => {
  let actualDelay;
  const writer = new CheckpointWriter({
    delay: 1000,
    scheduler: {
      schedule: (key, delay) => {
        actualDelay = delay;
      }
    },
    ready: () => true,
    revision: () => 64
  });
  writer.schedule();
  assert.equal(actualDelay, 1000);
});

test('checkpoint tracker advances monotonically and trims only covered bytes', () => {
  const tracker = new CheckpointJournalTracker();
  tracker.noteUpdate(1, 4, true, 0);
  tracker.noteUpdate(2, 8, false, 1);
  tracker.noteUpdate(3, 12, true, 2);
  assert.equal(tracker.bytesSinceCheckpoint, 24);
  tracker.noteCheckpoint(2);
  assert.equal(tracker.checkpointRevision, 2);
  assert.equal(tracker.bytesSinceCheckpoint, 12);
  for (const invalid of [NaN, -1, 0, 1, 2, 2.5, Infinity]) {
    tracker.noteCheckpoint(invalid);
    assert.equal(tracker.checkpointRevision, 2);
    assert.equal(tracker.bytesSinceCheckpoint, 12);
  }
  tracker.noteCheckpoint(3);
  assert.equal(tracker.bytesSinceCheckpoint, 0);
});

test('checkpoint tracker changes author only for new revisions and deduplicates bytes', () => {
  const tracker = new CheckpointJournalTracker();
  tracker.noteUpdate(2, 8, true, 1);
  tracker.noteUpdate(2, 8, false, 2);
  tracker.noteUpdate(1, 4, false, 2);
  assert.equal(tracker.isLatestAuthor, true);
  assert.equal(tracker.bytesSinceCheckpoint, 12);
  tracker.noteUpdate(3, 12, false, 2);
  assert.equal(tracker.isLatestAuthor, false);
  tracker.noteCheckpoint(3);
  tracker.noteUpdate(2, 8, true, 3);
  tracker.noteUpdate(3, 12, true, 3);
  assert.equal(tracker.bytesSinceCheckpoint, 0);
  assert.equal(tracker.isLatestAuthor, false);
  for (const invalid of [NaN, -1, 0, 1.5, Infinity]) {
    tracker.noteUpdate(invalid, 4, true, 3);
  }
  assert.equal(tracker.bytesSinceCheckpoint, 0);
  assert.equal(tracker.isLatestAuthor, false);
});

test('checkpoint tracker rebuilds sync accounting and resets the document session', () => {
  const tracker = new CheckpointJournalTracker();
  tracker.noteUpdate(1, 20, true, 0);
  tracker.resetFromSync(2, [
    { revision: 2, update: 'ignored' },
    { revision: 3, update: 'AQ==' },
    { revision: 3, update: 'AQ==' }
  ]);
  assert.equal(tracker.checkpointRevision, 2);
  assert.equal(tracker.bytesSinceCheckpoint, 4);
  assert.equal(tracker.isLatestAuthor, false);
  tracker.resetFromSync(1, [{ revision: 1, update: 'AQ==' }]);
  assert.equal(tracker.checkpointRevision, 2);
  assert.equal(tracker.bytesSinceCheckpoint, 0);
  tracker.noteUpdate(4, 4, true, 3);
  tracker.reset();
  assert.equal(tracker.checkpointRevision, 0);
  assert.equal(tracker.bytesSinceCheckpoint, 0);
  assert.equal(tracker.isLatestAuthor, false);
});

test('queue splits batches within the encoded limit and retains the tail', async () => {
  const sent = [];
  const f = fixture((update) => sent.push([...update]), { maxEncodedBytes: 8 });
  for (let i = 1; i <= 3; i++) f.queue.enqueue(Uint8Array.of(i, i, i));
  while (f.queue.pending.length) await f.tick();
  assert.deepEqual(sent, [
    [1, 1, 1, 2, 2, 2],
    [3, 3, 3]
  ]);
  assert.equal(f.queue.blocked, false);
});

test('a rejected batch retries singly but an oversized single update blocks', async () => {
  const sent = [];
  const f = fixture((update) => {
    sent.push([...update]);
    if (sent.length === 1) throw new Error('LimitExceeded');
  });
  f.queue.enqueue(Uint8Array.of(1));
  f.queue.enqueue(Uint8Array.of(2));
  await f.tick();
  assert.equal(f.queue.blocked, false);
  await f.tick();
  await f.tick();
  assert.deepEqual(sent, [[1, 2], [1], [2]]);
  const single = fixture(
    () => {
      throw new Error('LimitExceeded');
    },
    { maxEncodedBytes: 1 }
  );
  single.queue.enqueue(Uint8Array.of(1, 2));
  await single.tick();
  assert.equal(single.queue.blocked, true);
});

test('checkpoint size refusal waits for the revision retry interval', async () => {
  let revision = 10;
  let schedules = 0;
  let sends = 0;
  let permanent;
  const writer = new CheckpointWriter({
    retryAfterRevisions: 64,
    scheduler: {
      schedule() {
        schedules++;
      },
      cancel() {}
    },
    ready: () => true,
    capture: () => 1,
    current: () => true,
    revision: () => revision,
    payload() {
      throw new Error('LimitExceeded: Checkpoint size');
    },
    send() {
      sends++;
    },
    onSuccess() {},
    onError(error, value) {
      permanent = value;
    }
  });
  await writer.run();
  assert.equal(sends, 0);
  assert.equal(permanent, true);
  revision = 73;
  writer.schedule();
  assert.equal(schedules, 0);
  revision = 74;
  writer.schedule();
  assert.equal(schedules, 1);
});

test('sync predicates require every relevant readiness condition', () => {
  const state = {
    closed: false,
    joined: true,
    participating: true,
    hasDocument: true,
    synchronizing: false,
    connected: true,
    hasText: true,
    revision: 2,
    highestRevision: 2,
    pending: false,
    timer: false,
    inFlight: false,
    blocked: false,
    checkpointDue: true
  };
  assert.equal(canSendUpdates(state), true);
  assert.equal(canRecover(state), true);
  assert.equal(canWriteCheckpoint(state), true);
  for (const key of [
    'closed',
    'participating',
    'hasDocument',
    'synchronizing',
    'connected'
  ])
    assert.equal(canSendUpdates({ ...state, [key]: !state[key] }), false, key);
  for (const key of ['closed', 'joined', 'participating', 'hasDocument'])
    assert.equal(canRecover({ ...state, [key]: !state[key] }), false, key);
  for (const key of [
    'joined',
    'hasText',
    'pending',
    'timer',
    'inFlight',
    'blocked',
    'checkpointDue'
  ])
    assert.equal(canWriteCheckpoint({ ...state, [key]: !state[key] }), false, key);
  assert.equal(canWriteCheckpoint({ ...state, revision: 0 }), false);
  assert.equal(canWriteCheckpoint({ ...state, highestRevision: 3 }), false);
});

test('an unexpectedly large merge halves the prefix without dropping its tail', async () => {
  const sent = [];
  const f = fixture((update) => sent.push([...update]), {
    maxEncodedBytes: 8,
    merge: () => new Uint8Array(9)
  });
  f.queue.enqueue(Uint8Array.of(1));
  f.queue.enqueue(Uint8Array.of(2));
  await f.tick();
  assert.deepEqual(sent, [[1]]);
  assert.equal(f.queue.pending.length, 1);
  await f.tick();
  assert.deepEqual(sent, [[1], [2]]);
});

for (const Controller of [SnapshotRecovery, CheckpointWriter]) {
  for (const reset of [false, true]) {
    for (const fail of [false, true]) {
      test(`${Controller.name} releases stale flight (reset=${reset}, fail=${fail})`, async () => {
        let complete;
        let current = true;
        let successes = 0;
        let errors = 0;
        let scheduled = 0;
        const pending = () => new Promise((resolve, reject) => {
          complete = () => fail ? reject(new Error('late')) : resolve(true);
        });
        const controller = new Controller({
          scheduler: { schedule() { scheduled++; }, cancel() {} },
          active: () => true,
          ready: () => true,
          capture: () => 1,
          current: () => current,
          revisions: () => ({ DocumentRevision: 0, MetadataRevision: 0, PresenceRevision: 0 }),
          probe: pending,
          snapshot: async () => ({}),
          begin: () => 1,
          apply() {},
          end() {},
          revision: () => 1,
          payload: () => ({}),
          send: pending,
          onSuccess() { successes++; },
          onError() { errors++; }
        });
        const flight = controller.run();
        if (reset) controller.reset();
        else current = false;
        complete();
        await flight;
        assert.equal(controller.inFlight, false);
        assert.equal(successes, 0);
        assert.equal(errors, 0);
        assert.equal(scheduled, 0);
        current = true;
        controller.schedule();
        assert.equal(scheduled, 1);
      });
    }
  }
}

test('batch fallback covers only the refused prefix and preserves enqueue order', async () => {
  const sent = [];
  const f = fixture((update) => {
    sent.push([...update]);
    if (sent.length === 1) throw new Error('LimitExceeded');
  });
  for (const value of [1, 2, 3]) f.queue.enqueue(Uint8Array.of(value));
  await f.tick();
  assert.equal(f.queue.singleRemaining, 3);
  for (const value of [4, 5]) f.queue.enqueue(Uint8Array.of(value));
  while (f.queue.pending.length) await f.tick();
  assert.deepEqual(sent, [[1, 2, 3], [1], [2], [3], [4, 5]]);
  assert.equal(f.queue.singleRemaining, 0);
  f.queue.singleRemaining = 2;
  f.queue.reset();
  assert.equal(f.queue.singleRemaining, 0);
});

test('merge failures restore the prefix and retry individual updates', async () => {
  const sent = [];
  const f = fixture((update) => sent.push([...update]), {
    merge() { throw new Error('broken merge'); }
  });
  for (const value of [1, 2, 3]) f.queue.enqueue(Uint8Array.of(value));
  await f.tick();
  assert.deepEqual(f.errors, [false]);
  assert.deepEqual(f.queue.pending.map((item) => [...item]), [[1], [2], [3]]);
  assert.equal(f.timers.size, 1);
  while (f.queue.pending.length) await f.tick();
  assert.deepEqual(sent, [[1], [2], [3]]);
  assert.equal(f.queue.blocked, false);
});

test('transport reset preserves the checkpoint refusal interval until document disposal', () => {
  let revision = 10;
  let schedules = 0;
  const writer = new CheckpointWriter({
    scheduler: { schedule() { schedules++; }, cancel() {} },
    ready: () => true,
    revision: () => revision
  });
  writer.skippedRevision = 10;
  writer.reset({ keepSkipped: true });
  revision = 73;
  writer.schedule();
  assert.equal(schedules, 0);
  revision = 74;
  writer.schedule();
  assert.equal(schedules, 1);
  writer.reset();
  assert.equal(writer.skippedRevision, -1);
});
