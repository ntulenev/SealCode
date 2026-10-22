import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as Y from 'yjs';
import { base64FromUint8 } from '../../src/SealCode/wwwroot/encoding.js';
import { YjsSession } from '../../src/SealCode/wwwroot/yjs-session.js';
import { CheckpointJournalTracker } from '../../src/SealCode/wwwroot/room-sync.js';

function create() {
  const revisions = [];
  const model = { getValue: () => model.text?.toString() ?? '' };
  class Binding {
    constructor(text) {
      model.text = text;
    }
    destroy() {}
  }
  class Awareness {
    setLocalStateField() {}
    destroy() {}
  }
  const session = new YjsSession({
    Y,
    MonacoBinding: Binding,
    Awareness,
    BoundedUndoManager: null,
    CheckpointJournalTracker,
    editor: { getModel: () => model },
    getDisplayName: () => 'Alice',
    undoEnabled: () => false,
    cursorPositions: {},
    onRevision: (revision) => revisions.push(revision),
    onRemoteChange() {},
    onRemoteAuthor() {},
    onUpdate() {},
    onCheckpoint() {},
    onRecovery() {},
    onDispose() {}
  });
  session.initializeYjs('', null, [], 0, 0);
  return { session, revisions };
}

test('buffers updates during synchronization and flushes in revision order', () => {
  const { session, revisions } = create();
  const source = new Y.Doc();
  const updates = [];
  source.on('update', (update) => updates.push(base64FromUint8(update)));
  source.getText('monaco').insert(0, 'A');
  source.getText('monaco').insert(1, 'B');
  session.isSynchronizingYjs = true;
  session.receiveUpdate(updates[1], 2, 3, 'Bob');
  session.receiveUpdate(updates[0], 1, 2, 'Bob');
  session.flushBufferedYjsMessages();
  assert.equal(session.ytext.toString(), '');
  session.isSynchronizingYjs = false;
  session.flushBufferedYjsMessages();
  assert.equal(session.ytext.toString(), 'AB');
  assert.deepEqual(revisions, [2, 3]);
  assert.equal(session.currentDocumentRevision, 2);
  assert.equal(session.bufferedYjsMessages.length, 0);
  session.receiveUpdate(updates[0], 1, 2, 'Bob');
  assert.equal(session.checkpointJournal.updateBytes.size, 2);
  session.disposeYjs();
  source.destroy();
});

test('dispose destroys the document and clears revisions, buffer and history', () => {
  const { session } = create();
  const document = session.ydoc;
  session.pendingDocumentRevisions.add(10);
  session.bufferedYjsMessages.push({ updateBase64: 'AQ==', documentRevision: 10 });
  session.isSynchronizingYjs = true;
  session.highestDocumentRevisionSeen = 10;
  session.disposeYjs();
  assert.equal(document.isDestroyed, true);
  for (const name of ['ydoc', 'ytext', 'awareness', 'monacoBinding', 'undoHistory'])
    assert.equal(session[name], null);
  assert.equal(session.currentDocumentRevision, 0);
  assert.equal(session.highestDocumentRevisionSeen, 0);
  assert.equal(session.isSynchronizingYjs, false);
  assert.equal(session.pendingDocumentRevisions.size, 0);
  assert.equal(session.bufferedYjsMessages.length, 0);
});

test('bootstrap, merge, local changes, revision gaps and cursor adjustments retain session semantics', () => {
  const { session } = create();
  const sent = [];
  const recoveries = [];
  session.onUpdate = (update) => sent.push(update);
  session.onRecovery = (delay) => recoveries.push(delay);
  session.undoEnabled = () => true;
  class Undo {
    constructor(_, text, options) {
      this.text = text;
      this.options = options;
      this.destroyed = false;
    }
    destroy() {
      this.destroyed = true;
    }
  }
  session.BoundedUndoManager = Undo;
  session.initializeYjs('initial', null, [], 0, 0);
  assert.equal(session.ytext.toString(), 'initial');
  assert.equal(sent.length, 0);
  const source = new Y.Doc();
  const text = source.getText('monaco');
  text.insert(0, 'base');
  const checkpoint = base64FromUint8(Y.encodeStateAsUpdate(source));
  let update;
  source.on('update', (value) => {
    update = base64FromUint8(value);
  });
  text.insert(4, ' tail');
  session.initializeYjs('', checkpoint, [], 0, 0);
  assert.equal(session.undoHistory.options.trackedOrigin, session.monacoBinding);
  assert.equal(session.undoHistory.options.maxDepth, 100);
  assert.equal(
    session.mergeYjsSyncState(null, [{ Revision: 1, Update: update }], 1, 0),
    true
  );
  assert.equal(session.ytext.toString(), 'base tail');
  assert.equal(sent.length, 0);
  session.ytext.insert(0, 'local ');
  assert.equal(sent.length, 1);
  assert.equal(session.noteDocumentRevision(3), false);
  assert.deepEqual(recoveries, [250]);
  assert.equal(session.noteDocumentRevision(2), true);
  assert.equal(session.currentDocumentRevision, 3);
  assert.equal(session.noteDocumentRevision(3), false);
  assert.equal(session.noteDocumentRevision(-1), false);
  assert.deepEqual(
    session
      .normalizeYjsUpdates([
        { revision: 2, update: 'Ag==' },
        { Revision: 1, Update: 'AQ==' },
        null,
        { Revision: 0, Update: '' }
      ])
      .map((item) => item.revision),
    [1, 2]
  );
  assert.deepEqual(session.normalizeYjsUpdates(null), []);
  Object.assign(session.cursorPositions, { Alice: 2, Bob: 10, Charlie: 4 });
  session.applyRemoteCursorAdjustment([{ rangeOffset: 3, rangeLength: 2, text: 'ABC' }]);
  assert.deepEqual(session.cursorPositions, { Alice: 2, Bob: 11, Charlie: 6 });
  const undo = session.undoHistory;
  session.disposeYjs();
  assert.equal(undo.destroyed, true);
  source.destroy();
});

test('multiple cursor edits use offsets from the original document', () => {
  const { session } = create();
  Object.assign(session.cursorPositions, { Bob: 20, Carol: 7 });
  session.applyRemoteCursorAdjustment([
    { rangeOffset: 2, rangeLength: 3, text: '' },
    { rangeOffset: 10, rangeLength: 4, text: '' }
  ]);
  assert.deepEqual(session.cursorPositions, { Bob: 13, Carol: 4 });
  session.disposeYjs();
});
