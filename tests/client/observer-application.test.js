import test from 'node:test';
import assert from 'node:assert/strict';
import * as Y from 'yjs';
import { createObserverApplication, observerRetryDelay } from '../../src/SealCode/wwwroot/observer-application.js';

function deferred() {
  let resolve;
  const promise = new Promise(done => { resolve = done; });
  return { promise, resolve };
}

function fixture(options = {}) {
  const nodes = new Map();
  const element = () => ({ textContent: '', attrs: {}, children: [], classes: new Set(),
    setAttribute(key, value) { this.attrs[key] = value; },
    classList: { add() {}, remove() {}, toggle() {} },
    replaceChildren(...children) { this.children = children; }, append(...children) { this.children.push(...children); } });
  const document = { documentElement: { dataset: { theme: 'dark' } },
    querySelector: () => element(), createElement: element,
    getElementById(id) { if (!nodes.has(id)) nodes.set(id, element()); return nodes.get(id); } };
  const events = {};
  const timers = new Map();
  let time = 100000;
  let nextTimer = 0;
  const window = { location: { hash: '#' + (options.token ?? 'a'.repeat(43)), reload() { window.reloaded = true; } },
    addEventListener(name, callback) { events[name] = callback; },
    setTimeout(callback, delay) { timers.set(++nextTimer, { callback, at: time + delay }); return nextTimer; },
    clearTimeout(id) { timers.delete(id); },
    MutationObserver: class { constructor(callback) { window.themeChanged = callback; } observe() {} }
  };
  const handlers = {};
  const calls = [];
  const failures = new Map();
  const server = new Y.Doc();
  server.getText('monaco').insert(0, 'SELECT 1;');
  let revision = 0;
  const snapshot = overrides => ({ Type: 'Snapshot', Name: 'Interview', Kind: options.kind ?? 'code',
    Language: 'sql', Text: '', DocumentRevision: revision, CheckpointRevision: revision,
    YjsState: Buffer.from(Y.encodeStateAsUpdate(server)).toString('base64'), YjsUpdates: [],
    MetadataRevision: 0, PresenceRevision: 0, Users: ['Presenter'], ObserverCount: 2, ObserverRevision: 1,
    ...overrides });
  const remote = { Revisions: { DocumentRevision: 0, MetadataRevision: 0, PresenceRevision: 0 }, ObserverCount: 2, ObserverRevision: 1 };
  const connection = { state: 'Disconnected',
    on(name, callback) { handlers[name] = callback; },
    onclose(callback) { connection.closed = callback; },
    onreconnected(callback) { connection.reconnected = callback; },
    onreconnecting(callback) { connection.reconnecting = callback; },
    async start() {
      calls.push({ method: 'start' });
      if (options.startPending) await options.startPending;
      if (options.startError) throw options.startError;
      connection.state = 'Connected';
    },
    async stop() { calls.push({ method: 'stop' }); connection.state = 'Disconnected'; connection.closed(); },
    async invoke(method, ...args) {
      calls.push({ method, args });
      if (failures.has(method)) throw failures.get(method);
      return remote;
    }
  };
  const model = { value: '', setValue(value) { model.value = value; }, dispose() { model.disposed = true; } };
  const editor = { saveViewState: () => 'scroll-position', restoreViewState(value) { editor.view = value; }, dispose() { editor.disposed = true; } };
  const monaco = { editor: {
    createModel() { return model; },
    create(_host, config) { editor.config = config; return editor; },
    setModelLanguage(_model, language) { model.language = language; },
    setTheme(theme) { editor.theme = theme; }
  } };
  const board = { documents: [], setReadOnly(value) { board.readOnly = value; }, bind(doc) {
    board.documents.push(doc);
    return { destroy() { board.destroyed = true; } };
  } };
  const app = createObserverApplication({ window, document, connection, Y,
    createWhiteboardView: () => board,
    loadMonaco: async () => { if (options.editorPending) await options.editorPending; return monaco; },
    connectedState: 'Connected', now: () => time, random: () => 0.5 });
  const send = async message => { handlers.ObserverMessage(message); await app.idle(); };
  async function advance(ms) {
    const end = time + ms;
    while (true) {
      const next = [...timers].sort((a, b) => a[1].at - b[1].at).find(entry => entry[1].at <= end);
      if (!next) break;
      timers.delete(next[0]); time = next[1].at; await next[1].callback();
    }
    time = end;
  }
  function update(text = 'x', overrides = {}) {
    const before = Y.encodeStateVector(server);
    server.getText('monaco').insert(server.getText('monaco').length, text);
    revision++;
    return { Type: 'Update', DocumentRevision: revision,
      YjsUpdates: [{ Revision: revision, Update: Buffer.from(Y.encodeStateAsUpdate(server, before)).toString('base64') }],
      Language: 'sql', MetadataRevision: 0, PresenceRevision: 0, Users: ['Presenter'],
      ObserverCount: 2, ObserverRevision: 1, ...overrides };
  }
  const status = () => document.getElementById('watchStatus').textContent;
  const count = method => calls.filter(call => call.method === method).length;
  function close() { events.pagehide(); server.destroy(); }
  return { app, ready: app.ready, send, snapshot, update, model, editor, board, document, window, connection,
    status, calls, failures, remote, events, handlers, advance, timers, count, close };
}

test('guest code view applies CRDT in one direction, preserves scrolling and ignores older metadata', async t => {
  const f = fixture(); t.after(f.close); await f.ready;
  assert.equal(f.calls[1].method, 'JoinObserver');
  assert.equal(f.calls[1].args[0], 'a'.repeat(43));
  await f.send(f.snapshot());
  assert.equal(f.model.value, 'SELECT 1;');
  assert.equal(f.editor.config.readOnly, true); assert.equal(f.editor.config.domReadOnly, true);
  const changed = f.update(' -- shared', { Language: 'javascript', MetadataRevision: 2, PresenceRevision: 2, Users: ['Presenter', 'Alex'] });
  await f.send(changed);
  assert.equal(f.model.value, 'SELECT 1; -- shared'); assert.equal(f.editor.view, 'scroll-position');
  assert.equal(f.model.language, 'javascript');
  await f.send(changed); // Duplicate delivery must not duplicate text.
  await f.send({ ...changed, Language: 'sql', MetadataRevision: 1, PresenceRevision: 1, Users: [] });
  assert.equal(f.model.language, 'javascript');
  assert.equal(f.document.getElementById('participantsList').children.length, 2);
  f.document.documentElement.dataset.theme = 'light'; f.window.themeChanged(); assert.equal(f.editor.theme, 'vs');
  f.document.documentElement.dataset.theme = 'dark'; f.window.themeChanged(); assert.equal(f.editor.theme, 'vs-dark');
  f.model.setValue('local developer tools change');
  assert.equal(f.count('UpdateYjs'), 0); assert.equal(f.count('CheckpointYjs'), 0);
  await f.send({ Type: 'Count', ObserverCount: 7, ObserverRevision: 3 });
  assert.equal(f.document.getElementById('observerCount').textContent, '👁 7');
});

test('whiteboard snapshots use a readonly board and replace its previous binding', async t => {
  const f = fixture({ kind: 'whiteboard' }); t.after(f.close); await f.ready;
  await f.send(f.snapshot()); await f.send(f.update()); await f.send(f.snapshot());
  assert.equal(f.board.readOnly, true); assert.equal(f.board.documents.length, 2);
  assert.equal(f.board.destroyed, true); assert.equal(f.editor.config, undefined);
  assert.equal(f.board.documents[1].getText('monaco').toString(), 'SELECT 1;x');
});

test('empty-journal plaintext bootstrap and existing editor are reused for resynchronization', async t => {
  const f = fixture(); t.after(f.close); await f.ready;
  await f.send(f.snapshot({ YjsState: null, YjsUpdates: [], Text: 'Initial text' }));
  assert.equal(f.model.value, 'Initial text');
  await f.send(f.snapshot({ YjsState: null, Text: '', YjsUpdates: f.update().YjsUpdates }));
  assert(f.editor.config); // Replacement snapshot retains a single editor instance.
});

test('revision gaps and missing final edits request recovery without sending document changes', async t => {
  const f = fixture(); t.after(f.close); await f.ready; await f.send(f.snapshot());
  f.update('a'); const gap = f.update('b');
  await f.send(gap); await f.send(gap); // One pending recovery for the same gap.
  assert.equal(f.status(), 'Synchronizing…');
  await f.advance(1000); assert.equal(f.count('SyncObserver'), 1);
  await f.send(f.snapshot()); assert.equal(f.model.value, 'SELECT 1;ab');
  f.remote.Revisions.DocumentRevision = 3;
  await f.advance(15000);
  await f.advance(17000); assert(f.count('SyncObserver') > 1);
  assert.equal(f.count('UpdateYjs'), 0);
});

test('explicit resync, malformed state and malformed JSON keep the last view and retry', async t => {
  const f = fixture(); t.after(f.close); await f.ready; await f.send(JSON.stringify(f.snapshot()));
  await f.send(f.snapshot({ YjsState: 'AA==' }));
  assert.equal(f.model.value, 'SELECT 1;');
  await f.send('{broken-json'); await f.send({ Type: 'RequiresResync' });
  await f.advance(1000); assert.equal(f.count('SyncObserver'), 1);
  await f.send(f.snapshot()); assert.equal(f.status(), 'Live · Read only');
});

test('a missing tail after a contiguous batch also triggers recovery', async t => {
  const f = fixture(); t.after(f.close); await f.ready; await f.send(f.snapshot());
  await f.send({ ...f.update(), DocumentRevision: 2 });
  await f.advance(1000); assert.equal(f.count('SyncObserver'), 1);
});

test('offline/reconnect preserves readonly view and rejoins with the same capability', async t => {
  const f = fixture(); t.after(f.close); await f.ready; await f.send(f.snapshot());
  f.connection.state = 'Reconnecting'; f.connection.reconnecting();
  assert.match(f.status(), /out of date/);
  await f.advance(15000); assert.equal(f.count('GetObserverRevisions'), 0);
  await f.send({ Type: 'RequiresResync' }); await f.advance(1000);
  assert.equal(f.count('SyncObserver'), 0);
  f.connection.state = 'Connected'; await f.connection.reconnected();
  assert.equal(f.count('JoinObserver'), 2);
  await f.send(f.snapshot()); assert.equal(f.status(), 'Live · Read only');
});

for (const phase of ['start', 'JoinObserver']) {
  test(`transient ${phase} failures retry and preserve the guest limit message`, async t => {
    const options = phase === 'start' ? { startError: new Error('ObserverLimitReached') } : {};
    const f = fixture(options); t.after(f.close);
    if (phase === 'JoinObserver') f.failures.set(phase, new Error('ObserverLimitReached'));
    await f.ready; assert.match(f.status(), /guest limit/);
    delete options.startError; f.failures.clear();
    await f.advance(7500); assert(f.count('start') >= 2);
    await f.send(f.snapshot()); assert.equal(f.status(), 'Live · Read only');
  });
}

test('close errors retain capacity feedback and retry the transport only once', async t => {
  const f = fixture(); t.after(f.close); await f.ready;
  f.connection.state = 'Disconnected'; f.connection.closed(new Error('ObserverLimitReached'));
  f.connection.closed(); assert.match(f.status(), /guest limit/);
  await f.advance(7500); assert.equal(f.count('start'), 2);
});

for (const phase of ['JoinObserver', 'SyncObserver', 'GetObserverRevisions']) {
  test(`revocation during ${phase} ends retries and keeps the last document readable`, async t => {
    const f = fixture(); t.after(f.close);
    if (phase === 'JoinObserver') f.failures.set(phase, new Error('Forbidden'));
    await f.ready;
    if (phase !== 'JoinObserver') {
      await f.send(f.snapshot()); f.failures.set(phase, new Error('ObserverUnavailable'));
      if (phase === 'SyncObserver') { await f.send({ Type: 'RequiresResync' }); await f.advance(1000); }
      else await f.advance(15000);
    }
    assert.match(f.status(), /unavailable/);
    const calls = f.calls.length; await f.advance(100000); assert.equal(f.calls.length, calls);
    await f.send(f.snapshot()); assert.match(f.status(), /unavailable/);
  });
}

test('temporary recovery and probe failures back off and can recover', async t => {
  const f = fixture(); t.after(f.close); await f.ready; await f.send(f.snapshot());
  f.failures.set('SyncObserver', new Error('RateLimited'));
  await f.send({ Type: 'RequiresResync' }); await f.advance(1000);
  assert.match(f.status(), /Synchronizing|Waiting/);
  f.failures.clear(); await f.advance(1000); assert(f.count('SyncObserver') >= 2);
  await f.send(f.snapshot());
  f.failures.set('GetObserverRevisions', 'network'); await f.advance(15000);
  f.failures.clear(); await f.advance(17000); assert(f.count('SyncObserver') >= 3);
});

test('invalid links never connect, and page cleanup supports browser back/forward restoration', async () => {
  const f = fixture({ token: 'bad' }); await f.ready;
  assert.match(f.status(), /invalid/); assert.equal(f.count('start'), 0);
  f.events.pageshow({ persisted: false }); assert.equal(f.window.reloaded, undefined);
  f.events.pageshow({ persisted: true }); assert.equal(f.window.reloaded, true);
  f.close(); await f.send(f.snapshot()); assert.match(f.status(), /out of date/);
});

test('closing while the editor module is loading prevents late editor creation and a false live status', async () => {
  const pending = deferred(); const f = fixture({ editorPending: pending.promise }); await f.ready;
  f.handlers.ObserverMessage(f.snapshot()); await Promise.resolve();
  f.close(); pending.resolve(); await f.app.idle();
  assert.equal(f.editor.config, undefined); assert.match(f.status(), /out of date/);
  assert.equal(f.model.disposed, undefined);
});

test('guest buffers reject an oversized message and excessive messages during editor startup', async t => {
  const pending = deferred(); const f = fixture({ editorPending: pending.promise }); t.after(f.close); await f.ready;
  f.handlers.ObserverMessage(f.snapshot()); await Promise.resolve();
  for (let i = 0; i < 257; i++) f.handlers.ObserverMessage({ Type: 'Count', ObserverCount: i, ObserverRevision: i });
  f.handlers.ObserverMessage(' '.repeat(20 * 1024 * 1024 + 1));
  await f.advance(1000); assert.equal(f.count('SyncObserver'), 1);
  pending.resolve(); await f.app.idle(); assert(f.editor.config);
});

test('closing while the transport is starting cannot rejoin a hidden page', async () => {
  const pending = deferred(); const f = fixture({ startPending: pending.promise });
  f.close(); pending.resolve(); await f.ready;
  assert.equal(f.count('JoinObserver'), 0);
});

test('reconnect jitter stays bounded for short and arbitrarily long outages', () => {
  assert.equal(observerRetryDelay({ previousRetryCount: 0 }, () => 0), 800);
  assert.equal(Math.round(observerRetryDelay({ previousRetryCount: 0 }, () => 1)), 1200);
  assert.equal(observerRetryDelay({ previousRetryCount: 99 }, () => 1), 30000);
  assert(observerRetryDelay({ previousRetryCount: 1 }) >= 1600);
});
