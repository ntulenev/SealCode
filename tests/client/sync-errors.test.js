import test from 'node:test';
import assert from 'node:assert/strict';
import { SyncErrors } from '../../src/SealCode/wwwroot/sync-errors.js';

test('sync errors retain every kind and reveal the next highest priority', () => {
  const errors = new SyncErrors();
  assert.equal(errors.current(), null);
  const priority = ['update-size', 'checkpoint-size', 'rejoin', 'recovery', 'transport'];
  for (const kind of [...priority].reverse()) {
    assert.deepEqual(errors.show(kind, kind), { kind, message: kind });
  }
  errors.show('rejoin', 'another failure');
  assert.deepEqual(errors.clear('rejoin'), {
    kind: 'update-size',
    message: 'update-size'
  });
  errors.show('rejoin', 'rejoin');
  for (let i = 0; i < priority.length; i++) {
    const next = priority[i + 1];
    assert.deepEqual(
      errors.clear(priority[i]),
      next ? { kind: next, message: next } : null
    );
  }
  assert.equal(errors.clear('rejoin'), null);
});
