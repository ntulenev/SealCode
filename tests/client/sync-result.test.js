import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  readSyncResult,
  applyJoinDocument
} from '../../src/SealCode/wwwroot/sync-result.js';

test('sync results accept both naming conventions and normalize defaults', () => {
  const lower = {
    name: 'Room',
    language: 'sql',
    text: 'text',
    version: 2,
    users: ['Alice'],
    createdBy: 'admin',
    yjsState: 'AQ==',
    yjsUpdates: [],
    documentRevision: 3,
    checkpointRevision: 1
  };
  const upper = Object.fromEntries(
    Object.entries(lower).map(([key, value]) => [
      key[0].toUpperCase() + key.slice(1),
      value
    ])
  );
  assert.deepEqual(readSyncResult(lower), lower);
  assert.deepEqual(readSyncResult(upper), lower);
  assert.equal(readSyncResult({ Version: 'invalid' }).version, 0);
  assert.deepEqual(readSyncResult({}).yjsUpdates, []);
  assert.equal(readSyncResult({}).documentRevision, 0);
});

test('join initialization and reconnect merging share the normalized payload', () => {
  const calls = [];
  const session = {
    ydoc: null,
    initializeYjs: (...args) => calls.push(['initialize', ...args]),
    mergeYjsSyncState: (...args) => {
      calls.push(['merge', ...args]);
      return true;
    }
  };
  const sync = readSyncResult({ Text: 'text', YjsState: 'AQ==', DocumentRevision: 2 });
  applyJoinDocument(session, sync, false);
  assert.deepEqual(calls[0], ['initialize', 'text', 'AQ==', [], 2, 0]);
  session.ydoc = {};
  applyJoinDocument(session, sync, true);
  assert.deepEqual(calls[1], ['merge', 'AQ==', [], 2, 0]);
  session.mergeYjsSyncState = () => false;
  assert.throws(() => applyJoinDocument(session, sync, true), /Failed to merge/);
});
