import test from 'node:test';
import assert from 'node:assert/strict';
import * as Y from 'yjs';
import { BoundedUndoManager } from '../../src/SealCode/wwwroot/bounded-undo-manager.js';
import { compactCheckpoint } from '../../src/SealCode/wwwroot/compact-checkpoint.js';

test('checkpoint discards undo-only content without clearing local undo', () => {
  const doc = new Y.Doc();
  const text = doc.getText('monaco');
  const origin = {};
  const undo = new BoundedUndoManager(Y, text, { trackedOrigin: origin });
  for (let i = 0; i < 2; i++) {
    undo.stopCapturing();
    doc.transact(() => {
      text.delete(0, text.length);
      text.insert(0, (i === 0 ? 'x' : '界').repeat(500000));
    }, origin);
  }
  const raw = Y.encodeStateAsUpdate(doc);
  const compact = compactCheckpoint(Y, doc);
  assert.ok(compact.state.length < raw.length);
  assert.equal(compact.text.length, 500000);
  assert.ok(undo.canUndo());
  const late = new Y.Doc();
  Y.applyUpdate(late, compact.state);
  assert.equal(late.getText('monaco').toString(), compact.text);
  undo.undo();
  assert.equal(text.toString(), 'x'.repeat(500000));
  undo.destroy();
  late.destroy();
  doc.destroy();
});
