import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createRoomConnection } from '../../src/SealCode/wwwroot/room-connection.js';
import { createMonacoAdapter } from '../../src/SealCode/wwwroot/monaco-adapter.js';
import { editorFixture } from './helpers/room-fixture.js';

test('connection shares pending starts, retries after rejection and adjusts reconnect to connectivity', async () => {
  let resolve;
  let starts = 0;
  let online = true;
  let policy;
  let url;
  const hub = { state: 'Disconnected', start() { starts++; return new Promise((done) => { resolve = done; }); } };
  const signalR = {
    HubConnectionState: { Disconnected: 'Disconnected' },
    HubConnectionBuilder: class {
      withUrl(value) { url = value; return this; }
      withAutomaticReconnect(value) { policy = value; return this; }
      build() { return hub; }
    }
  };
  const controller = createRoomConnection(signalR, '/roomHub', () => online);
  assert.equal(url, '/roomHub');
  assert.equal(hub.serverTimeoutInMilliseconds, 15000);
  assert.equal(hub.keepAliveIntervalInMilliseconds, 5000);
  assert.equal(policy.nextRetryDelayInMilliseconds(), 3000);
  online = false;
  assert.equal(policy.nextRetryDelayInMilliseconds(), 2000);
  const first = controller.start(), second = controller.start();
  assert.equal(starts, 1);
  resolve();
  assert.deepEqual(await Promise.all([first, second]), [true, true]);
  hub.start = () => { starts++; return Promise.reject(new Error('offline')); };
  assert.equal(await controller.start(), false);
  hub.start = () => { starts++; return Promise.resolve(); };
  assert.equal(await controller.start(), true);
  hub.state = 'Connected';
  assert.equal(await controller.start(), true);
  assert.equal(starts, 3);
});

test('Monaco adapter preserves its model across theme changes and routes undo to collaboration', async () => {
  for (const theme of ['light', 'dark']) {
    const f = editorFixture(theme);
    let undos = 0, redos = 0;
    const result = createMonacoAdapter(f.monaco, f.host, { undo: () => undos++, redo: () => redos++ });
    assert.equal(result.model.language, 'plaintext');
    assert.equal(result.editor.options.readOnly, true);
    assert.equal(result.editor.options.emptySelectionClipboard, false);
    assert.equal(result.editor.options.theme, theme === 'light' ? 'vs' : 'vs-dark');
    await result.model.undo();
    await result.model.redo();
    assert.deepEqual([undos, redos], [1, 1]);
    f.document.documentElement.dataset.theme = theme === 'light' ? 'dark' : 'light';
    await f.view.fire('sealcode:themechange');
    assert.equal(result.editor.getModel(), result.model);
    assert.equal(f.themes.length, 2);
    f.callbacks.dispose();
    await f.view.fire('sealcode:themechange');
    assert.equal(f.themes.length, 2);
  }
});
