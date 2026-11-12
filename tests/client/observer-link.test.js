import test from 'node:test';
import assert from 'node:assert/strict';
import { mountObserverLink } from '../../src/SealCode/wwwroot/observer-link.js';

function fixture() {
  const nodes = [];
  const document = { createElement(tag) {
    const node = { tag, children: [], handlers: {}, classList: { add() {} }, setAttribute() {}, select() { this.selected = true; },
      append(...values) { this.children.push(...values); }, addEventListener(name, callback) { this.handlers[name] = callback; } };
    nodes.push(node); return node;
  }, querySelector: () => ({ append(panel) { nodes.push(panel); } }) };
  const responses = [];
  const requests = [];
  const fetch = async (url, options) => {
    requests.push({ url, options });
    if (url === '/admin/token') return { ok: true, json: async () => ({ Token: 'disposable-csrf' }) };
    return responses.shift();
  };
  const window = { confirm: () => true, location: { origin: 'https://test.invalid' }, navigator: { clipboard: { writeText: async () => {} } } };
  return { nodes, responses, requests, document, fetch, window, roomId: 'editor-room', node: text => nodes.find(n => n.textContent === text) };
}
const tick = () => new Promise(resolve => setImmediate(resolve));

test('link controls are absent for unauthorized sessions and disabled deployments', async () => {
  const f = fixture();
  f.responses.push({ ok: false }); await mountObserverLink(f); assert.equal(f.nodes.length, 0);
  f.responses.push({ ok: true, json: async () => ({ Enabled: false }) }); await mountObserverLink(f); assert.equal(f.nodes.length, 0);
});
test('owner creates, copies and revokes a link without retaining credentials in URLs', async () => {
  const f = fixture();
  f.responses.push({ ok: true, json: async () => ({ Enabled: true, MaxPerRoom: 20 }) });
  await mountObserverLink(f);
  const copied = [];
  f.window.navigator.clipboard.writeText = async value => copied.push(value);
  const copy = f.node('Copy link');
  const disable = f.node('Disable guest access');
  assert.equal(copy.hidden, true); assert.equal(copy.disabled, true);
  assert.equal(disable.hidden, true); assert.equal(disable.disabled, true);
  await copy.handlers.click(); disable.handlers.click(); await tick();
  assert.deepEqual(copied, []); assert.equal(f.requests.length, 1);
  f.responses.push({ ok: true, json: async () => ({ Url: '/watch#guest-capability' }) });
  f.node('Create / replace link').handlers.click();
  assert.equal(copy.disabled, true);
  await copy.handlers.click();
  await tick();
  const input = f.nodes.find(n => n.tag === 'input');
  assert.equal(input.value, 'https://test.invalid/watch#guest-capability');
  assert.equal(copy.hidden, false); assert.equal(copy.disabled, false);
  assert.equal(disable.hidden, false); assert.equal(disable.disabled, false);
  assert(f.requests.some(r => r.options?.headers?.['X-CSRF-TOKEN'] === 'disposable-csrf'));
  await f.node('Copy link').handlers.click(); assert(f.node('Guest link copied.'));
  assert.deepEqual(copied, ['https://test.invalid/watch#guest-capability']);
  f.window.navigator.clipboard.writeText = async () => { throw new Error('Denied'); };
  await f.node('Copy link').handlers.click(); assert(input.selected);
  f.responses.push({ ok: true });
  f.node('Disable guest access').handlers.click(); await tick();
  assert.equal(input.value, ''); assert.equal(input.hidden, true);
  assert.equal(copy.hidden, true); assert.equal(copy.disabled, true);
  assert.equal(disable.hidden, true); assert.equal(disable.disabled, true);
  await copy.handlers.click();
  assert(f.node('Guest access disabled.'));
});

test('a link retained by the server can be revoked after reload without copying an unknown token', async () => {
  const f = fixture();
  f.responses.push({ ok: true, json: async () => ({ Enabled: true, HasLink: true, MaxPerRoom: 20 }) });
  await mountObserverLink(f);
  const copy = f.node('Copy link');
  const disable = f.node('Disable guest access');
  assert.equal(copy.hidden, true); assert.equal(copy.disabled, true);
  assert.equal(disable.hidden, false); assert.equal(disable.disabled, false);
  f.responses.push({ ok: false });
  disable.handlers.click(); await tick();
  assert.equal(disable.hidden, false); assert.equal(disable.disabled, false);
  f.responses.push({ ok: true });
  disable.handlers.click(); await tick();
  assert.equal(disable.hidden, true); assert.equal(disable.disabled, true);
});
test('canceled rotation makes no mutation and failed requests show a bounded error', async () => {
  const f = fixture();
  f.responses.push({ ok: true, json: async () => ({ Enabled: true, MaxPerRoom: 20 }) });
  await mountObserverLink(f);
  f.window.confirm = () => false;
  f.node('Create / replace link').handlers.click(); await tick(); assert.equal(f.requests.length, 1);
  f.window.confirm = () => true;
  f.responses.push({ ok: false });
  f.node('Create / replace link').handlers.click(); await tick();
  assert(f.node('Could not update guest access. Check your admin session.'));
});
