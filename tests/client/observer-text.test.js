import { test } from 'node:test';
import assert from 'node:assert/strict';
import { applyObserverTextDelta } from '../../src/SealCode/wwwroot/observer-text.js';

function fixture(value) {
  const edits = [], resets = [];
  const model = {getValue: () => value,
    getPositionAt: offset => ({lineNumber: 1, column: offset + 1}),
    applyEdits(items) {
      edits.push(items);
      for (const item of [...items].reverse()) {
        value = value.slice(0, item.range.startColumn - 1) + item.text + value.slice(item.range.endColumn - 1);
      }
    }, setValue(next) { resets.push(next); value = next; }};
  return {model, edits, resets};
}

test('observer applies inserts, deletes and retains without resetting the document', () => {
  const f = fixture('abcdef');
  applyObserverTextDelta(f.model, {toString: () => 'aXYdef!'},
    [{retain: 1}, {insert: 'XY'}, {delete: 2}, {retain: 3}, {insert: '!'}]);
  assert.equal(f.model.getValue(), 'aXYdef!');
  assert.equal(f.edits[0].length, 2);
  assert.deepEqual(f.resets, []);
  applyObserverTextDelta(f.model, {toString: () => 'adef!'}, [{retain: 1}, {delete: 2}]);
  assert.equal(f.model.getValue(), 'adef!');
});

test('observer falls back exactly once when the model missed a change', () => {
  const f = fixture('stale');
  applyObserverTextDelta(f.model, {toString: () => 'authoritative'}, [{retain: 5}]);
  assert.deepEqual(f.resets, ['authoritative']);
});

test('observer handles model adapters and embedded values through the fallback', () => {
  const f = fixture('old');
  delete f.model.applyEdits;
  applyObserverTextDelta(f.model, {toString: () => 'new'}, [{insert: {embed: true}}]);
  assert.deepEqual(f.resets, ['new']);
});

test('minimal read-only model adapters receive one authoritative fallback', () => {
  for (const missing of ['getPositionAt', 'getValue']) {
    const f = fixture('old');
    delete f.model[missing];
    applyObserverTextDelta(f.model, {toString: () => 'new'}, []);
    assert.deepEqual(f.resets, ['new']);
  }
});

test('observer falls back once when Monaco rejects stale edit ranges', () => {
  const f = fixture('stale');
  f.model.applyEdits = () => { throw new Error('invalid range'); };
  applyObserverTextDelta(f.model, {toString: () => 'authoritative'}, [{insert: 'new'}]);
  assert.deepEqual(f.resets, ['authoritative']);
});
