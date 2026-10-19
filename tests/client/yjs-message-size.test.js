import assert from 'node:assert/strict';
import test from 'node:test';
import * as Y from 'yjs';
import { DEFAULT_MAX_YJS_MESSAGE_BYTES, fitsYjsMessage } from '../../src/SealCode/wwwroot/yjs-message-size.js';

test('a 500,000 character document fits the advertised checkpoint budget', () => {
  const doc = new Y.Doc();
  const value = 'x'.repeat(500000);
  doc.getText('monaco').insert(0, value);
  const state = Buffer.from(Y.encodeStateAsUpdate(doc)).toString('base64');
  const args = ['room', state, value, 1];
  assert.equal(fitsYjsMessage('CheckpointYjs', args, 1024 * 1024), false);
  assert.equal(fitsYjsMessage('CheckpointYjs', args, DEFAULT_MAX_YJS_MESSAGE_BYTES), true);
  doc.destroy();
});

test('message checks account for UTF-8 and JSON escaping rather than character count', () => {
  assert.equal(fitsYjsMessage('CheckpointYjs', ['room', '', 'x'.repeat(5000), 1], 8192), true);
  assert.equal(fitsYjsMessage('CheckpointYjs', ['room', '', '😀'.repeat(2500), 1], 8192), false);
  assert.equal(fitsYjsMessage('CheckpointYjs', ['room', '', '\u0001'.repeat(2000), 1], 8192), false);
});

test('an oversized edit is rejected before transmission', () => {
  assert.equal(fitsYjsMessage('UpdateYjs', ['room', 'A'.repeat(DEFAULT_MAX_YJS_MESSAGE_BYTES)], DEFAULT_MAX_YJS_MESSAGE_BYTES), false);
});
