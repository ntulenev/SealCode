import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as Y from 'yjs';
import { createJournalRecovery, classifyJournalRecoveryError } from '../../src/SealCode/wwwroot/journal-recovery.js';

function fixture(overrides = {}) {
  const errors = [], checkpoints = [];
  const options = {
    Y, yjs: { yjsSession: 1, checkpointJournal: { noteCheckpoint: n => checkpoints.push(n) } },
    updateSender: { pausedForJournal: true, resumeAfterCheckpoint() { this.pausedForJournal = false; } },
    lifecycle: { participating: true, closed: false, closeRoom: reason => errors.push(reason) },
    connection: { state: 'connected', invoke: async method => method === 'SyncYjs'
      ? { DocumentRevision: 2, YjsUpdates: [], Text: '', Version: 1 } : true },
    signalR: { HubConnectionState: { Connected: 'connected' } }, roomId: 'room', isBoard: false,
    getLimits: () => ({maxEncodedCheckpointBytes: 1000, maxSnapshotCharacters: 1000, maxYjsMessageBytes: 1000}),
    clearSyncError() {}, showSyncError: (...args) => errors.push(args), setEditorReadOnly() {},
    ...overrides
  };
  return { options, errors, checkpoints, recover: createJournalRecovery(options) };
}

test('journal compaction checkpoints the authoritative state and resumes edits', async () => {
  const f = fixture();
  await f.recover();
  assert.deepEqual(f.checkpoints, [2]);
  assert.equal(f.options.updateSender.pausedForJournal, false);
  assert.deepEqual(f.errors, []);
});

test('journal recovery classifies permanent errors without exposing arbitrary objects', async () => {
  assert.equal(classifyJournalRecoveryError(new Error('LimitExceeded')), true);
  assert.equal(classifyJournalRecoveryError(new Error('too large')), true);
  assert.equal(classifyJournalRecoveryError(null), false);
  assert.equal(classifyJournalRecoveryError('LimitExceeded'), false);
  const f = fixture({getLimits: () => ({maxEncodedCheckpointBytes: 0, maxSnapshotCharacters: 0, maxYjsMessageBytes: 1})});
  await f.recover();
  assert.equal(f.errors[0][0], 'checkpoint-size');
  assert.equal(f.options.updateSender.pausedForJournal, true);
});

test('journal retry uses the injected timer and stops after a session change', async () => {
  let attempts = 0;
  const f = fixture({ connection: { state: 'connected', invoke: async method => {
    if (method === 'SyncYjs') { attempts++; throw new Error('temporary'); }
  } }, setTimer(callback, delay) {
    assert.equal(delay, 1100);
    f.options.yjs.yjsSession++;
    callback();
  } });
  await f.recover();
  assert.equal(attempts, 1);
});
