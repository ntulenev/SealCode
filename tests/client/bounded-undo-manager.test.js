import assert from 'node:assert/strict';
import test from 'node:test';

import * as Y from 'yjs';

import { BoundedUndoManager } from '../../src/SealCode/wwwroot/bounded-undo-manager.js';

const remoteOrigin = Object.freeze({ source: 'remote' });

function createClient() {
  const doc = new Y.Doc();
  const text = doc.getText('monaco');
  const localOrigin = Object.freeze({ source: 'local' });
  const history = new BoundedUndoManager(Y, text, {
    trackedOrigin: localOrigin,
    maxDepth: 100,
    maxEstimatedBytes: 8 * 1024 * 1024,
    captureTimeout: 500
  });
  const updates = [];
  doc.on('update', (update, origin) => {
    if (origin !== remoteOrigin) updates.push(update);
  });
  return { doc, text, localOrigin, history, updates };
}

function insert(client, index, value) {
  client.doc.transact(() => client.text.insert(index, value), client.localOrigin);
  client.history.stopCapturing();
}

function syncNewUpdates(source, target, startIndex) {
  for (const update of source.updates.slice(startIndex)) {
    Y.applyUpdate(target.doc, update, remoteOrigin);
  }
}

test('remote edits are excluded from the local undo stack', () => {
  const client = createClient();
  insert(client, 0, 'A');
  client.doc.transact(() => client.text.insert(1, 'B'), remoteOrigin);

  assert.equal(client.history.undoDepth, 1);
  client.history.undo();
  assert.equal(client.text.toString(), 'B');
  client.history.destroy();
  client.doc.destroy();
});

test('undo removes only the local concurrent insertion and converges', () => {
  const alice = createClient();
  const bob = createClient();
  insert(alice, 0, 'alice');
  insert(bob, 0, 'bob');

  syncNewUpdates(alice, bob, 0);
  syncNewUpdates(bob, alice, 0);
  assert.equal(alice.text.toString(), bob.text.toString());

  const aliceBeforeUndo = alice.updates.length;
  alice.history.undo();
  syncNewUpdates(alice, bob, aliceBeforeUndo);

  assert.equal(alice.text.toString(), 'bob');
  assert.equal(bob.text.toString(), 'bob');
});

test('simultaneous undo of each client converges without resurrecting either edit', () => {
  const alice = createClient();
  const bob = createClient();
  insert(alice, 0, 'alice');
  insert(bob, 0, 'bob');
  syncNewUpdates(alice, bob, 0);
  syncNewUpdates(bob, alice, 0);

  const aliceBeforeUndo = alice.updates.length;
  const bobBeforeUndo = bob.updates.length;
  alice.history.undo();
  bob.history.undo();
  syncNewUpdates(alice, bob, aliceBeforeUndo);
  syncNewUpdates(bob, alice, bobBeforeUndo);

  assert.equal(alice.text.toString(), '');
  assert.equal(bob.text.toString(), '');
});

test('redo after a remote edit preserves the remote edit', () => {
  const alice = createClient();
  const bob = createClient();
  insert(alice, 0, 'A');
  syncNewUpdates(alice, bob, 0);
  const aliceBeforeUndo = alice.updates.length;
  alice.history.undo();
  syncNewUpdates(alice, bob, aliceBeforeUndo);

  const bobBeforeInsert = bob.updates.length;
  insert(bob, bob.text.length, 'B');
  syncNewUpdates(bob, alice, bobBeforeInsert);

  const aliceBeforeRedo = alice.updates.length;
  alice.history.redo();
  syncNewUpdates(alice, bob, aliceBeforeRedo);

  assert.equal(alice.text.toString(), bob.text.toString());
  assert.equal(alice.text.toString().includes('A'), true);
  assert.equal(alice.text.toString().includes('B'), true);
});

test('visible undo depth is capped at one hundred grouped local operations', () => {
  const client = createClient();
  for (let index = 0; index < 105; index += 1) {
    insert(client, client.text.length, 'x');
  }

  assert.equal(client.history.undoDepth, 100);
  for (let index = 0; index < 100; index += 1) client.history.undo();
  assert.equal(client.text.length, 5);
  assert.equal(client.history.canUndo(), false);
});

test('memory guard clears the complete history safely', () => {
  const doc = new Y.Doc();
  const text = doc.getText('monaco');
  const localOrigin = Object.freeze({ source: 'local' });
  const history = new BoundedUndoManager(Y, text, {
    trackedOrigin: localOrigin,
    maxDepth: 100,
    maxEstimatedBytes: 10
  });
  doc.transact(() => text.insert(0, 'large edit'), localOrigin);

  assert.equal(history.canUndo(), false);
  assert.equal(history.lastClearReason, 'memory-limit');
});

test('deleting a large bootstrap document clears history despite a tiny update', () => {
  const doc = new Y.Doc();
  const text = doc.getText('monaco');
  text.insert(0, 'x'.repeat(10 * 1024 * 1024));
  const localOrigin = {};
  const history = new BoundedUndoManager(Y, text, { trackedOrigin: localOrigin });
  let updateBytes = 0;
  doc.on('update', (update, origin) => {
    if (origin === localOrigin) updateBytes = update.length;
  });

  doc.transact(() => text.delete(0, text.length), localOrigin);

  assert.ok(updateBytes < 100);
  assert.equal(text.toString(), '');
  assert.equal(history.canUndo(), false);
  assert.equal(history.lastClearReason, 'memory-limit');
  assert.equal(history.estimatedBytes, 0);
  assert.ok(doc.store.clients.get(doc.clientID).every((item) => !item.keep));
  assert.ok(doc.store.clients.get(doc.clientID).every((item) => typeof item.content?.str !== 'string'));
  doc.transact(() => text.insert(0, 'fresh'), localOrigin);
  history.undo();
  assert.equal(text.toString(), '');
  history.redo();
  assert.equal(text.toString(), 'fresh');
  history.destroy();
  doc.destroy();
});

test('the memory budget includes discarded deletions and clears redo as well', () => {
  const doc = new Y.Doc();
  const text = doc.getText('monaco');
  text.insert(0, 'x'.repeat(3000));
  const localOrigin = {};
  const history = new BoundedUndoManager(Y, text, {
    trackedOrigin: localOrigin,
    maxDepth: 1,
    maxEstimatedBytes: 5000
  });
  for (let index = 0; index < 2; index += 1) {
    doc.transact(() => text.delete(0, 500), localOrigin);
    history.stopCapturing();
  }
  assert.equal(history.discardedStackItems.length, 1);
  const budgetBeforeUndo = history.estimatedBytes;
  history.undo();
  assert.equal(history.canRedo(), true);
  assert.ok(history.estimatedBytes > budgetBeforeUndo);

  // Redo deletes the restored text, and must also be charged to the budget.
  history.redo();

  assert.equal(history.lastClearReason, 'memory-limit');
  assert.equal(history.canUndo(), false);
  assert.equal(history.canRedo(), false);
  assert.equal(history.discardedStackItems.length, 0);
  assert.ok(doc.store.clients.get(doc.clientID)
    .filter((item) => item.deleted)
    .every((item) => typeof item.content?.str !== 'string'));
  history.destroy();
  doc.destroy();
});

test('remote deletions do not consume the local history budget', () => {
  const client = createClient();
  client.doc.transact(() => client.text.insert(0, 'x'.repeat(10000)), remoteOrigin);
  insert(client, client.text.length, 'local');
  const budget = client.history.estimatedBytes;

  client.doc.transact(() => client.text.delete(0, 10000), remoteOrigin);

  assert.equal(client.history.estimatedBytes, budget);
  client.history.undo();
  assert.equal(client.text.toString(), '');
  client.history.destroy();
  client.doc.destroy();
});
