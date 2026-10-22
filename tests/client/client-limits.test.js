import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  readClientLimits,
  checkCheckpointSize,
  timeoutSignal
} from '../../src/SealCode/wwwroot/client-limits.js';

test('fetch timeout delegates to the native signal factory', (context) => {
  const expected = new AbortController().signal;
  const native = context.mock.method(AbortSignal, 'timeout', () => expected);
  assert.equal(timeoutSignal(1234), expected);
  assert.deepEqual(native.mock.calls[0].arguments, [1234]);
});

test('fetch timeout falls back to an abort controller and timer', (context) => {
  const original = AbortSignal.timeout;
  context.after(() => {
    AbortSignal.timeout = original;
  });
  AbortSignal.timeout = undefined;
  let expire;
  const signal = timeoutSignal(1234, (callback, delay) => {
    assert.equal(delay, 1234);
    expire = callback;
  });
  assert.ok(signal instanceof AbortSignal);
  assert.equal(signal.aborted, false);
  expire();
  assert.equal(signal.aborted, true);
});

test('fallback uses the browser timer and aborts only at the deadline', (context) => {
  const original = AbortSignal.timeout;
  context.after(() => {
    AbortSignal.timeout = original;
  });
  AbortSignal.timeout = undefined;
  context.mock.timers.enable({ apis: ['setTimeout'] });
  const signal = timeoutSignal(100);
  context.mock.timers.tick(99);
  assert.equal(signal.aborted, false);
  context.mock.timers.tick(1);
  assert.equal(signal.aborted, true);
});

test('client limits accept either casing and reject invalid values', () => {
  const defaults = readClientLimits();
  for (const value of ['4096', -1, 0, 1.5, NaN, Infinity, null]) {
    assert.deepEqual(
      readClientLimits({ Limits: { MaxEncodedUpdateBytes: value } }),
      defaults
    );
  }
  for (const key of ['Limits', 'limits']) {
    assert.equal(
      readClientLimits({ [key]: { maxEncodedUpdateBytes: 1234 } }).maxEncodedUpdateBytes,
      1234
    );
    assert.equal(
      readClientLimits({ [key]: { MaxEncodedCheckpointBytes: 4321 } })
        .maxEncodedCheckpointBytes,
      4321
    );
  }
  assert.equal(readClientLimits({ maxYjsMessageBytes: 2048 }).maxYjsMessageBytes, 2048);
});

for (const [size, limit] of [
  ['stateBase64Length', 'maxEncodedCheckpointBytes'],
  ['textLength', 'maxSnapshotCharacters']
]) {
  test(`checkpoint size checks the exact ${limit} boundary`, () => {
    const limits = readClientLimits();
    for (const offset of [-1, 0, 1]) {
      const check = () =>
        checkCheckpointSize(
          { stateBase64Length: 0, textLength: 0, [size]: limits[limit] + offset },
          limits
        );
      if (offset > 0) assert.throws(check, /LimitExceeded: Checkpoint size/);
      else assert.doesNotThrow(check);
    }
  });
}
