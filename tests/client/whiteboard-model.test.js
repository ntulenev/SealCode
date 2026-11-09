import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as Y from 'yjs';
import { bounds, isShape, normalizeShape, bindWhiteboard, shapeTypes } from '../../src/SealCode/wwwroot/whiteboard-model.js';
import { compactCheckpoint } from '../../src/SealCode/wwwroot/compact-checkpoint.js';
import { BoundedUndoManager } from '../../src/SealCode/wwwroot/bounded-undo-manager.js';
import { YjsSession } from '../../src/SealCode/wwwroot/yjs-session.js';
import { CheckpointJournalTracker } from '../../src/SealCode/wwwroot/room-sync.js';

const shape = (id = 'one', type = 'rectangle') => ({ id, type, x: 100, y: 120, width: -80, height: -60, text: 'Service' });

test('shared shapes validate geometry and labels without trusting document contents', () => {
  for (const type of shapeTypes) assert.ok(isShape(shape('id', type)));
  for (const value of [null, {}, { ...shape(), id: 1 }, { ...shape(), id: 'x'.repeat(101) }, { ...shape(), type: '<script>' }, { ...shape(), text: null }, { ...shape(), text: 'x'.repeat(4001) }, { ...shape(), x: Infinity }, { ...shape(), y: NaN }, { ...shape(), width: 1e9 }]) assert.equal(isShape(value), false);
  assert.deepEqual(bounds(shape()), { x: 20, y: 60, width: 80, height: 60 });
  assert.deepEqual(normalizeShape(shape()), { ...shape(), x: 20, y: 60, width: 80, height: 60 });
  for (const type of ['arrow', 'bidirectional-arrow', 'line']) assert.deepEqual(normalizeShape(shape('one', type)), shape('one', type));
});

test('concurrent shapes converge, selective undo preserves remote edits, checkpoints retain diagrams', () => {
  const alice = new Y.Doc(), bob = new Y.Doc();
  let renders = 0;
  const binding = bindWhiteboard(alice, () => renders++);
  const undo = new BoundedUndoManager(Y, binding.scope, { trackedOrigin: binding });
  alice.transact(() => binding.scope.set('one', shape()), binding);
  bob.getMap('whiteboard').set('two', shape('two', 'database'));
  Y.applyUpdate(alice, Y.encodeStateAsUpdate(bob));
  Y.applyUpdate(bob, Y.encodeStateAsUpdate(alice));
  assert.equal(binding.scope.size, 2);
  undo.undo();
  assert.equal(binding.scope.has('one'), false);
  assert.equal(binding.scope.has('two'), true);
  undo.redo();
  assert.equal(binding.scope.size, 2);
  const checkpoint = compactCheckpoint(Y, alice);
  const recovered = new Y.Doc();
  Y.applyUpdate(recovered, checkpoint.state);
  assert.deepEqual(recovered.getMap('whiteboard').toJSON(), binding.scope.toJSON());
  assert.equal(checkpoint.text, '');
  binding.destroy();
  const last = renders;
  binding.scope.set('three', shape('three'));
  assert.equal(renders, last);
  undo.destroy();
  alice.destroy(); bob.destroy(); recovered.destroy();
});

test('workspace binding initializes without Monaco and merges through the normal Yjs session', () => {
  let current;
  const updates = [];
  const session = new YjsSession({
    Y, MonacoBinding: null, Awareness: class { setLocalStateField() {} destroy() {} },
    BoundedUndoManager, CheckpointJournalTracker,
    bindDocument(doc) { current = bindWhiteboard(doc, () => {}); return current; },
    getDisplayName: () => 'Alice', undoEnabled: () => true, cursorPositions: {},
    onRevision() {}, onRemoteChange() {}, onRemoteAuthor() {}, onUpdate: (u) => updates.push(u),
    onCheckpoint() {}, onRecovery() {}, onDispose() {}
  });
  session.initializeYjs('', null, [], 0, 0);
  session.ydoc.transact(() => current.scope.set('one', shape()), current);
  assert.equal(updates.length, 1);
  const remote = new Y.Doc();
  remote.getMap('whiteboard').set('two', shape('two'));
  const state = Buffer.from(Y.encodeStateAsUpdate(remote)).toString('base64');
  assert.equal(session.mergeYjsSyncState(state, [], 0, 0), true);
  assert.equal(current.scope.size, 2);
  session.undoHistory.undo();
  assert.equal(current.scope.has('two'), true);
  assert.equal(current.scope.has('one'), false);
  session.disposeYjs();
  remote.destroy();
});
