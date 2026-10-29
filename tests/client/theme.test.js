import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { runInNewContext } from 'node:vm';
import { bindEditorTheme } from '../../src/SealCode/wwwroot/editor-theme.js';

const scriptPath = fileURLToPath(new URL('../../src/SealCode/wwwroot/theme.js', import.meta.url));
const source = readFileSync(scriptPath, 'utf8');
const key = 'sealcode.theme';

function fixture({ saved, readyState = 'loading', blocked, iconOnly = false } = {}) {
  const data = new Map(saved ? [[key, saved]] : []);
  const windowHandlers = {};
  const documentHandlers = {};
  const root = { dataset: {} };
  const buttons = Array.from({ length: 2 }, () => ({
    attributes: {}, label: {}, handlers: {},
    setAttribute(name, value) { this.attributes[name] = value; },
    querySelector() { return iconOnly ? null : this.label; },
    addEventListener(name, handler) { this.handlers[name] = handler; }
  }));
  const storage = {
    getItem(name) { if (blocked === 'read') throw new Error('denied'); return data.get(name) ?? null; },
    setItem(name, value) { if (blocked === 'write') throw new Error('quota'); data.set(name, value); }
  };
  let changes = 0;
  const window = {
    get localStorage() { if (blocked === 'access') throw new Error('denied'); return storage; },
    addEventListener: (name, handler) => { windowHandlers[name] = handler; },
    dispatchEvent: () => { changes++; }
  };
  const document = {
    documentElement: root, readyState,
    querySelectorAll: () => buttons,
    addEventListener: (name, handler) => { documentHandlers[name] = handler; }
  };
  runInNewContext(source, { window, document, Event }, { filename: scriptPath });
  return { root, data, buttons, storage, windowHandlers, documentHandlers, changes: () => changes };
}

test('dark is the default and invalid stored choices are ignored', () => {
  for (const saved of [undefined, 'system', 'sepia', 'LIGHT'])
    assert.equal(fixture({ saved }).root.dataset.theme, 'dark');
});

test('saved light is applied before DOM readiness and both controls stay synchronized', () => {
  const page = fixture({ saved: 'light' });
  assert.equal(page.root.dataset.theme, 'light');
  page.documentHandlers.DOMContentLoaded();
  assert.equal(page.buttons[0].attributes['aria-label'], 'Switch to dark theme');
  page.buttons[0].handlers.click();
  assert.equal(page.root.dataset.theme, 'dark');
  assert.equal(page.data.get(key), 'dark');
  assert.equal(page.buttons[1].label.textContent, 'Light');
  page.buttons[1].handlers.click();
  assert.equal(page.root.dataset.theme, 'light');
  assert.equal(page.buttons[0].label.textContent, 'Dark');
  assert.equal(page.changes(), 4);
});

test('blocked storage access, reads or writes do not prevent switching themes', () => {
  for (const blocked of ['access', 'read', 'write']) {
    const page = fixture({ blocked, readyState: 'complete', iconOnly: true });
    page.buttons[0].handlers.click();
    assert.equal(page.root.dataset.theme, 'light');
  }
});

test('other tabs update the theme, including removal and clear, but unrelated storage is ignored', () => {
  const page = fixture();
  page.data.set(key, 'light');
  page.windowHandlers.storage({ storageArea: {}, key });
  page.windowHandlers.storage({ storageArea: page.storage, key: 'unrelated' });
  assert.equal(page.root.dataset.theme, 'dark');
  page.windowHandlers.storage({ storageArea: page.storage, key });
  assert.equal(page.root.dataset.theme, 'light');
  page.data.delete(key);
  page.windowHandlers.storage({ storageArea: page.storage, key });
  assert.equal(page.root.dataset.theme, 'dark');
  page.data.set(key, 'light');
  page.windowHandlers.storage({ storageArea: page.storage, key });
  page.data.clear();
  page.windowHandlers.storage({ storageArea: page.storage, key: null });
  assert.equal(page.root.dataset.theme, 'dark');
  const blocked = fixture({ blocked: 'access' });
  blocked.windowHandlers.storage({ storageArea: null, key });
  assert.equal(blocked.root.dataset.theme, 'dark');
});

test('editor themes follow the page palette and unsubscribe when disposed', () => {
  const view = new EventTarget();
  const root = { dataset: { theme: 'dark' } };
  view.getComputedStyle = () => ({ getPropertyValue: () => root.dataset.theme === 'light' ? ' #ffffff ' : ' #141a23 ' });
  const definitions = [];
  const applied = [];
  const monaco = { editor: {
    defineTheme: (name, theme) => definitions.push({ name, theme }),
    setTheme: (name) => applied.push(name)
  } };
  const dispose = bindEditorTheme(monaco, { defaultView: view, documentElement: root });
  assert.equal(definitions[0].theme.base, 'vs-dark');
  assert.equal(definitions[0].theme.colors['editor.background'], '#141a23');
  root.dataset.theme = 'light';
  view.dispatchEvent(new Event('sealcode:themechange'));
  assert.equal(definitions[1].theme.base, 'vs');
  assert.equal(definitions[1].theme.colors['editor.background'], '#ffffff');
  assert.deepEqual(applied, ['sealcode-dark', 'sealcode-light']);
  dispose();
  view.dispatchEvent(new Event('sealcode:themechange'));
  assert.equal(applied.length, 2);
});
