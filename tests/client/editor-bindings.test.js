import { test } from 'node:test';
import assert from 'node:assert/strict';
import { editorFixture, Element } from './helpers/room-fixture.js';
import { installEditorBindings } from '../../src/SealCode/wwwroot/editor-bindings.js';

test('editor bindings stop history capture and preserve keyboard and copy guards', async () => {
  const f = editorFixture();
  let captures = 0, undos = 0, lastSelection, readonlyText = '';
  const yjs = {undoHistory: {stopCapturing() { captures++; }}, applyRemoteCursorAdjustment() {}};
  installEditorBindings({ ...f, editorHost: f.host, NodeType: Element,
    yjs, presence: {hideRemoteCaretsTemporarily() {}, markActiveEditor() {}, markSelecting() {}, markCopying() {}},
    connection: {state: 'connected', invoke: async () => {}}, connectedState: 'connected', roomId: 'room',
    getDisplayName: () => 'Alice', getMaxSnapshotCharacters: () => 3,
    getCheckpointBlockedAtLength: () => null, clearCheckpointBlock() {},
    getLastSelection: () => lastSelection, setLastSelection: value => { lastSelection = value; },
    showSyncError: (_, text) => { readonlyText = text; }, clearSyncError: () => { readonlyText = ''; },
    scheduleCursorSend() {}, hasNonEmptyEditorSelection: () => false,
    undo: () => { undos++; }, redo() {}, setTimer: callback => callback() });
  f.callbacks.paste();
  assert.equal(captures, 1);
  const event = {key: 'z', ctrlKey: true, preventDefault() {}, stopImmediatePropagation() {}};
  f.callbacks.key({browserEvent: event, preventDefault() {}, stopPropagation() {}});
  assert.equal(undos, 1);
  f.model.value = 'large';
  f.callbacks.content({changes: []});
  assert.match(readonlyText, /3-character/);
  f.model.value = 'ok';
  f.callbacks.content({changes: []});
  assert.equal(readonlyText, '');
  let prevented = false;
  await f.document.fire('copy', {preventDefault() { prevented = true; }});
  assert.equal(prevented, true);
});
