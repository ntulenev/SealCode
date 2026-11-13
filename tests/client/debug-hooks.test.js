import { test } from 'node:test';
import assert from 'node:assert/strict';
import { installDebugHooks } from '../../src/SealCode/wwwroot/debug-hooks.js';

test('debug hooks require the automation flag', async () => {
  for (const webdriver of [undefined, false, true]) {
    const window = { navigator: { webdriver } };
    const options = { window, yjs: { ydoc: null }, lifecycle: { joined: false } };
    installDebugHooks(options);
    assert.equal(typeof window.__debugYjsSend, webdriver === true ? 'function' : 'undefined');
    assert.equal(typeof window.__debugUndoState, webdriver === true ? 'function' : 'undefined');
    assert.equal(typeof window.__debugReconnect, webdriver === true ? 'function' : 'undefined');
    if (webdriver === true) {
      assert.equal(window.__debugYjsSend(), false);
      assert.equal(await window.__debugReconnect(), false);
    }
  }
});
