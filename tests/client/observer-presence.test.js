import test from 'node:test';
import assert from 'node:assert/strict';
import { createObserverPresence } from '../../src/SealCode/wwwroot/observer-presence.js';

test('viewer count is compact, revisioned, accessible and hides zero', () => {
  const attributes = {};
  const hidden = new Set();
  const badge = { classList: { toggle(name, value) { if (value) hidden.add(name); else hidden.delete(name); } }, setAttribute(name, value) { attributes[name] = value; } };
  const update = createObserverPresence({ querySelector: () => ({}), getElementById: () => badge });
  update(20, 4);
  assert.equal(badge.textContent, '👁 20');
  assert.match(attributes['aria-label'], /Observers: 20/);
  update(2, 3); assert.equal(badge.textContent, '👁 20');
  update(-1, 5); update(NaN, 5); update(1, NaN);
  assert.equal(badge.textContent, '👁 20');
  update(0, 5); assert(hidden.has('hidden'));
});

test('viewer badge is created once and absent surfaces are harmless', () => {
  let appended;
  const badge = { setAttribute() {}, classList: { toggle() {} } };
  const update = createObserverPresence({ querySelector: () => ({ append(value) { appended = value; } }), getElementById: () => null, createElement: () => badge });
  assert.equal(appended, badge);
  update(1, 1); assert.equal(badge.textContent, '👁 1');
  createObserverPresence({ getElementById: () => null })(3, 2);
});
