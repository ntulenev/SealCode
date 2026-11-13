import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as Y from 'yjs';
import { createObserverApplication } from '../../src/SealCode/wwwroot/observer-application.js';

function fixture() {
  const nodes = new Map(), handlers = {}, events = {}, resets = [], views = [];
  const element = () => ({textContent: '', classList: {add() {}, remove() {}, toggle() {}},
    replaceChildren() {}, setAttribute() {}});
  const document = {documentElement: {dataset: {theme: 'dark'}},
    getElementById(id) {
      if (!nodes.has(id)) nodes.set(id, element());
      return nodes.get(id);
    }, createElement: element};
  const window = {location: {hash: '#' + 'a'.repeat(43)},
    setTimeout: () => 1, clearTimeout() {},
    addEventListener: (name, handler) => { events[name] = handler; },
    MutationObserver: class { observe() {} }};
  const calls = [];
  const connection = {state: 'Connected', on: (name, handler) => { handlers[name] = handler; },
    onclose() {}, onreconnected() {}, onreconnecting() {}, start: async () => {}, stop: async () => {},
    invoke: async name => { calls.push(name); }};
  let value = '';
  const model = {getValue: () => value, getPositionAt: offset => ({lineNumber: 1, column: offset + 1}),
    // Deliberately miss edits to exercise the required full-text repair.
    applyEdits() {}, setValue(next) { resets.push(next); value = next; }, dispose() {}};
  const editor = {saveViewState: () => 'viewport', restoreViewState: view => views.push(view), dispose() {}};
  const monaco = {editor: {createModel: () => model, create: () => editor, setModelLanguage() {}}};
  const app = createObserverApplication({window, document, connection, Y,
    loadMonaco: async () => monaco, connectedState: 'Connected', createWhiteboardView() { throw new Error('code only'); }});
  const source = new Y.Doc();
  source.getText('monaco').insert(0, 'A');
  const metadata = {Language: 'sql', MetadataRevision: 0, PresenceRevision: 0, Users: []};
  const send = async message => { handlers.ObserverMessage(message); await app.idle(); };
  async function start() {
    await app.ready;
    await send({Type: 'Snapshot', Name: 'Room', Kind: 'code', ...metadata,
      Text: '', YjsState: Buffer.from(Y.encodeStateAsUpdate(source)).toString('base64'),
      YjsUpdates: [], DocumentRevision: 0, CheckpointRevision: 0, Version: 1});
    resets.length = 0;
    views.length = 0;
  }
  return {model, source, resets, views, calls, start, send,
    update: updates => ({Type: 'Update', ...metadata, YjsUpdates: updates,
      DocumentRevision: updates.at(-1)?.Revision ?? 0, ObserverCount: 1, ObserverRevision: 1}),
    close() { events.pagehide(); source.destroy(); }};
}

test('observer reconciles an update even when no Y.Text event is emitted', async t => {
  const f = fixture(); t.after(f.close); await f.start();
  f.model.setValue('locally divergent');
  f.resets.length = 0;
  await f.send(f.update([]));
  assert.equal(f.model.getValue(), 'A');
  assert.deepEqual(f.resets, ['A']);
  assert.deepEqual(f.views, ['viewport']);
  assert.deepEqual(f.calls, ['JoinObserver']);
});

test('observer repairs a batched message only once after all document updates', async t => {
  const f = fixture(); t.after(f.close); await f.start();
  const updates = [];
  f.source.on('update', update => updates.push({Revision: updates.length + 1,
    Update: Buffer.from(update).toString('base64')}));
  f.source.getText('monaco').insert(1, 'B');
  f.source.getText('monaco').insert(2, 'C');
  await f.send(f.update(updates));
  assert.equal(f.model.getValue(), 'ABC');
  assert.deepEqual(f.resets, ['ABC']);
  assert.deepEqual(f.views, ['viewport']);
  assert.deepEqual(f.calls, ['JoinObserver']);
});
