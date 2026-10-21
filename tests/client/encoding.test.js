import { test } from 'node:test';
import assert from 'node:assert/strict';
import { base64FromUint8, uint8FromBase64 } from '../../src/SealCode/wwwroot/encoding.js';

test('encoding round trips empty, binary and large payloads', () => {
  for (const length of [0, 1, 256, 300_000]) {
    const bytes = Uint8Array.from({ length }, (_, index) => index % 256);
    assert.deepEqual(uint8FromBase64(base64FromUint8(bytes)), bytes);
  }
  assert.equal(base64FromUint8(new Uint8Array()), '');
});
