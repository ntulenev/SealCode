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
  f.responses.push({ ok: true, json: async () => ({ Url: '/watch#guest-capability' }) });
  f.node('Create / replace link').handlers.click(); await tick();
  const input = f.nodes.find(n => n.tag === 'input');
  assert.equal(input.value, 'https://test.invalid/watch#guest-capability');
  assert(f.requests.some(r => r.options?.headers?.['X-CSRF-TOKEN'] === 'disposable-csrf'));
  await f.node('Copy link').handlers.click(); assert(f.node('Guest link copied.'));
  f.window.navigator.clipboard.writeText = async () => { throw new Error('Denied'); };
  await f.node('Copy link').handlers.click(); assert(input.selected);
  f.responses.push({ ok: true });
  f.node('Disable guest access').handlers.click(); await tick();
  assert.equal(input.value, ''); assert.equal(input.hidden, true);
  assert(f.node('Guest access disabled.'));
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
