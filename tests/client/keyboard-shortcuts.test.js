import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  isCopyShortcut,
  isUndoShortcut,
  isRedoShortcut,
  isUndoRedoShortcut,
  isPasteShortcut
} from '../../src/SealCode/wwwroot/keyboard-shortcuts.js';

test('Ctrl and Cmd shortcuts preserve shift, code and layout handling', () => {
  for (const modifier of ['ctrlKey', 'metaKey']) {
    const event = { [modifier]: true };
    assert.equal(isCopyShortcut({ ...event, key: 'C' }), true);
    assert.equal(isPasteShortcut({ ...event, key: 'v' }), true);
    assert.equal(isUndoShortcut({ ...event, key: 'я', code: 'KeyZ' }), true);
    assert.equal(isUndoRedoShortcut({ ...event, key: 'z' }), true);
    assert.equal(isRedoShortcut({ ...event, key: 'z', shiftKey: true }), true);
    assert.equal(isRedoShortcut({ ...event, key: 'н', code: 'KeyY' }), true);
    assert.equal(isUndoShortcut({ ...event, key: 'z', shiftKey: true }), false);
    assert.equal(isCopyShortcut({ ...event, key: 'c', altKey: true }), false);
    assert.equal(isPasteShortcut({ ...event, key: 'v', altKey: true }), false);
  }
});

test('missing events, modifiers and unrelated keys are ignored', () => {
  for (const fn of [
    isCopyShortcut,
    isUndoShortcut,
    isRedoShortcut,
    isUndoRedoShortcut,
    isPasteShortcut
  ]) {
    for (const event of [
      null,
      {},
      { key: 'z' },
      { ctrlKey: true, key: 'a' },
      { ctrlKey: true, key: 'z', altKey: true }
    ]) {
      assert.equal(fn(event), false);
    }
  }
});
