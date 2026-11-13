import { test } from 'node:test';
import assert from 'node:assert/strict';
import { Element } from './helpers/room-fixture.js';
import { createSyncErrorView } from '../../src/SealCode/wwwroot/sync-error-view.js';

test('error view retains priority and offers a local text download only for size errors', async () => {
  const syncError = new Element(), button = new Element(), link = new Element('a');
  const blobs = [], revoked = [], timers = [];
  const view = createSyncErrorView({ syncError, downloadLocalText: button, roomId: 'room',
    document: { createElement: () => link, body: new Element() },
    getModel: () => ({getValue: () => 'unsent text'}),
    BlobType: class { constructor(parts) { blobs.push(parts); } },
    urlApi: {createObjectURL: () => 'blob:test', revokeObjectURL: value => revoked.push(value)},
    setTimer: (callback, delay) => timers.push({callback, delay}) });
  view.showSyncError('transport', 'Offline');
  assert.equal(button.classList.contains('hidden'), true);
  view.showSyncError('text-size', 'Too large');
  assert.equal(syncError.textContent, 'Too large');
  assert.equal(button.classList.contains('hidden'), false);
  await button.click();
  assert.deepEqual(blobs, [['unsent text']]);
  assert.equal(link.download, 'sealcode-room.txt');
  assert.equal(timers[0].delay, 1000);
  timers[0].callback();
  assert.deepEqual(revoked, ['blob:test']);
  view.clearSyncError('text-size');
  assert.equal(syncError.textContent, 'Offline');
  view.clearSyncError('transport');
  assert.equal(syncError.classList.contains('hidden'), true);
});
