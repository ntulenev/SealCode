import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  HubErrorCode,
  classifyHubError,
  createHubError
} from '../../src/SealCode/wwwroot/hub-errors.js';

test('classifies every local error and stable wire code', () => {
  for (const code of Object.values(HubErrorCode)) {
    assert.equal(classifyHubError(createHubError(code, 'failure')), code);
  }
  assert.equal(Object.isFrozen(HubErrorCode), true);
});

test('preserves size matching and recognizes SignalR wrappers', () => {
  assert.equal(
    classifyHubError(new Error('message too large')),
    HubErrorCode.LimitExceeded
  );
  assert.equal(
    classifyHubError(
      "An unexpected error occurred invoking 'X' on the server. HubException: LimitExceeded: Update size"
    ),
    HubErrorCode.LimitExceeded
  );
  assert.equal(
    classifyHubError('HubException: Room not found'),
    HubErrorCode.RoomNotFound
  );
});

test('unknown and empty errors have no category', () => {
  for (const error of [null, undefined, '', new Error('unknown')]) {
    assert.equal(classifyHubError(error), null);
  }
});
