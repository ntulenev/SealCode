import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readClientLimits, checkCheckpointSize } from '../../src/SealCode/wwwroot/client-limits.js';

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
      const check = () => checkCheckpointSize(
        { stateBase64Length: 0, textLength: 0, [size]: limits[limit] + offset },
        limits
      );
      if (offset > 0) assert.throws(check, /LimitExceeded: Checkpoint size/);
      else assert.doesNotThrow(check);
    }
  });
}
