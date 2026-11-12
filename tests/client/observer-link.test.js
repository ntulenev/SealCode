import test from 'node:test';
import assert from 'node:assert/strict';
import { mountObserverLink } from '../../src/SealCode/wwwroot/observer-link.js';

function fixture(Channel) {
  const nodes = [];
  const events = {};
  const documentEvents = {};
  const timers = new Map();
  let nextTimer = 0;
  let currentState;
  const document = { createElement(tag) {
    const node = { tag, children: [], handlers: {}, classList: { add() {} }, setAttribute() {}, select() { this.selected = true; },
      append(...values) { this.children.push(...values); }, addEventListener(name, callback) { this.handlers[name] = callback; } };
    nodes.push(node); return node;
  }, querySelector: () => ({ append(panel) { nodes.push(panel); } }),
  addEventListener(name, callback) { documentEvents[name] = callback; },
  removeEventListener(name) { delete documentEvents[name]; } };
  const responses = [];
  const requests = [];
  const permissionResponses = [];
  const permissionRequests = [];
  const fetch = async (url, options) => {
    if (url.endsWith('/permissions')) {
      permissionRequests.push({ url, options });
      const response = permissionResponses.shift() ?? { ok: true, json: async () => ({ CanDelete: true }) };
      if (response instanceof Error) throw response;
      return response;
    }
    requests.push({ url, options });
    if (url === '/admin/token') return { ok: true, json: async () => ({ Token: 'disposable-csrf' }) };
    let response = responses.shift();
    if (response instanceof Error) throw response;
    if (!response && !options?.method && currentState) response = { ok: true, json: async () => currentState };
    if (response?.ok && options?.method === 'DELETE') currentState = { Enabled: true, HasLink: false, Url: null };
    if (!response?.json) return response;
    const json = response.json;
    return { ...response, json: async () => {
      const value = await json();
      currentState = options?.method === 'POST' ? { Enabled: true, HasLink: true, Url: value.Url } : value;
      return value;
    } };
  };
  const window = { BroadcastChannel: Channel, confirm: () => true,
    setTimeout(callback) { timers.set(++nextTimer, callback); return nextTimer; }, clearTimeout(id) { timers.delete(id); },
    addEventListener(name, callback) { events[name] = callback; }, removeEventListener(name) { delete events[name]; },
    location: { origin: 'https://test.invalid', reload() {} }, navigator: { clipboard: { writeText: async () => {} } } };
  return { nodes, responses, requests, permissionResponses, permissionRequests, document, fetch, window, events, documentEvents, timers,
    roomId: 'editor-room', node: text => nodes.find(n => n.textContent === text) };
}
const tick = () => new Promise(resolve => setImmediate(resolve));

function messaging() {
  const channels = [];
  class Channel {
    constructor(name) { this.name = name; channels.push(this); }
    messages = [];
    postMessage(data) { this.messages.push(data); }
    close() { this.closed = true; }
    receive(data) { return this.onmessage({ data }); }
  }
  return { Channel, channels };
}
const activeLink = url => ({ ok: true, json: async () => ({ Enabled: true, HasLink: !!url, Url: url, MaxPerRoom: 20 }) });

for (const permission of [
  { ok: true, json: async () => ({ CanDelete: false }) },
  { ok: true, json: async () => ({}) },
  { ok: false }
]) {
  test('participants without management permission never request the protected link or start synchronization', async () => {
    const f = fixture();
    f.permissionResponses.push(permission);
    await mountObserverLink(f);
    assert.equal(f.nodes.length, 0); assert.equal(f.requests.length, 0); assert.equal(f.timers.size, 0);
    assert.equal(f.permissionRequests[0].options.cache, 'no-store');
  });
}

test('an unavailable discovery request fails closed without contacting private endpoints', async () => {
  const f = fixture();
  f.permissionResponses.push(new TypeError('offline'));
  await assert.rejects(mountObserverLink(f), /offline/);
  assert.equal(f.requests.length, 0); assert.equal(f.nodes.length, 0);
});

test('a panel clears permissions before private reads and automatically recovers when management access returns', async () => {
  const f = fixture();
  f.responses.push(activeLink('/watch#current')); await mountObserverLink(f);
  f.permissionResponses.push({ ok: true, json: async () => ({ CanDelete: false }) });
  f.events.focus(); await tick();
  assert.equal(f.requests.length, 1);
  assert.equal(f.nodes.find(n => n.tag === 'details').hidden, true);
  assert.equal(f.nodes.find(n => n.tag === 'input').value, '');
  assert.equal(f.node('Copy link').hidden, true);
  f.responses.push(activeLink('/watch#current'));
  [...f.timers.values()][0](); await tick();
  assert.equal(f.nodes.find(n => n.tag === 'details').hidden, false);
  assert.equal(f.node('Copy link').hidden, false);
});

test('closing a tab during permission discovery cannot update controls or request a private link later', async () => {
  const f = fixture();
  f.responses.push(activeLink('/watch#current')); await mountObserverLink(f);
  let release;
  f.permissionResponses.push({ ok: true, json: () => new Promise(resolve => { release = resolve; }) });
  f.events.focus(); await tick(); f.events.pagehide();
  release({ CanDelete: false }); await tick();
  assert.equal(f.requests.length, 1); assert.equal(f.timers.size, 0);
  assert.equal(f.nodes.find(n => n.tag === 'input').value, 'https://test.invalid/watch#current');
  assert.equal(f.permissionRequests.at(-1).options.signal.aborted, true);
});

test('cross-tab hints only trigger authoritative reads for the same room and never carry a token', async () => {
  const { Channel, channels } = messaging();
  const f = fixture(Channel);
  f.responses.push(activeLink(null)); await mountObserverLink(f);
  channels[0].receive(null); channels[0].receive({ roomId: 'another-room', Url: '/watch#forged' });
  assert.equal(f.requests.length, 1);
  f.responses.push(activeLink('/watch#created-in-another-tab'));
  channels[0].receive({ roomId: f.roomId, Url: '/watch#forged' }); await tick();
  assert.equal(f.nodes.find(n => n.tag === 'input').value, 'https://test.invalid/watch#created-in-another-tab');
  assert.equal(f.node('Copy link').hidden, false);
  f.responses.push({ ok: true, json: async () => ({ Url: '/watch#local-rotation' }) });
  f.node('Create / replace link').handlers.click(); await tick();
  assert.deepEqual(channels[0].messages, [{ roomId: f.roomId }]);
  assert.equal(f.requests.at(-1).options.cache, 'no-store');
});

test('polling, focus, visibility and panel opening reconcile state without cross-tab messaging', async () => {
  const f = fixture();
  f.responses.push(activeLink(null)); await mountObserverLink(f);
  const panel = f.nodes.find(n => n.tag === 'details');
  panel.handlers.toggle();
  f.document.visibilityState = 'hidden'; f.documentEvents.visibilitychange();
  assert.equal(f.requests.length, 1);
  f.responses.push(activeLink('/watch#created'));
  [...f.timers.values()][0](); await tick();
  assert.equal(f.node('Copy link').hidden, false);
  f.responses.push(activeLink('/watch#replaced'));
  f.events.focus(); await tick();
  assert.equal(f.nodes.find(n => n.tag === 'input').value, 'https://test.invalid/watch#replaced');
  f.responses.push(activeLink(null));
  f.document.visibilityState = 'visible'; f.documentEvents.visibilitychange(); await tick();
  assert.equal(f.node('Copy link').hidden, true);
  f.responses.push(activeLink('/watch#reopened'));
  panel.open = true; panel.handlers.toggle(); await tick();
  assert.equal(f.node('Copy link').hidden, false);
  assert.equal(f.timers.size, 1);
});

test('an unavailable or failing BroadcastChannel falls back to polling and mutation still succeeds', async () => {
  const f = fixture(class { constructor() { throw new Error('blocked'); } });
  f.responses.push(activeLink('/watch#old')); await mountObserverLink(f);
  assert.equal(f.timers.size, 1);
  const { Channel, channels } = messaging();
  const g = fixture(Channel);
  g.responses.push(activeLink('/watch#old')); await mountObserverLink(g);
  channels[0].postMessage = () => { throw new Error('channel closed'); };
  g.responses.push({ ok: true }); g.node('Disable guest access').handlers.click(); await tick();
  assert.equal(g.node('Copy link').hidden, true);
  assert.equal(g.node('Create / replace link').disabled, false);
});

test('a stale read followed by several notifications is reconciled once and copy waits for that state', async () => {
  const { Channel, channels } = messaging();
  const f = fixture(Channel);
  f.responses.push(activeLink('/watch#old')); await mountObserverLink(f);
  const copied = [];
  f.window.navigator.clipboard.writeText = async value => copied.push(value);
  let release;
  f.responses.push({ ok: true, json: () => new Promise(resolve => { release = resolve; }) });
  const pending = f.node('Copy link').handlers.click(); await tick();
  channels[0].receive({ roomId: f.roomId }); channels[0].receive({ roomId: f.roomId });
  f.responses.push(activeLink('/watch#latest'));
  release({ Enabled: true, HasLink: true, Url: '/watch#old' });
  await pending;
  assert.deepEqual(copied, ['https://test.invalid/watch#latest']);
  assert.equal(f.requests.length, 3);
});

test('a concurrent remote rotation wins over the local command result and duplicate actions are ignored', async () => {
  const { Channel, channels } = messaging();
  const f = fixture(Channel);
  f.responses.push(activeLink(null)); await mountObserverLink(f);
  let release;
  f.responses.push({ ok: true, json: () => new Promise(resolve => { release = resolve; }) });
  f.node('Create / replace link').handlers.click(); await tick();
  f.node('Create / replace link').handlers.click();
  channels[0].receive({ roomId: f.roomId });
  f.responses.push(activeLink('/watch#newer-remote-rotation'));
  release({ Url: '/watch#local-command' }); await tick();
  assert.equal(f.nodes.find(n => n.tag === 'input').value, 'https://test.invalid/watch#newer-remote-rotation');
  assert.equal(f.requests.filter(r => r.options?.method === 'POST').length, 1);
});

for (const status of [401, 403, 404, 500]) {
  test(`failed automatic synchronization clears cached tokens and recovers after HTTP ${status}`, async () => {
    const f = fixture();
    f.responses.push(activeLink('/watch#old')); await mountObserverLink(f);
    f.responses.push({ ok: false, status }); f.events.focus(); await tick();
    assert.equal(f.nodes.find(n => n.tag === 'input').value, '');
    assert.equal(f.node('Copy link').hidden, true);
    if (status !== 500) assert.equal(f.nodes.find(n => n.tag === 'details').hidden, true);
    f.responses.push(activeLink('/watch#recovered')); [...f.timers.values()][0](); await tick();
    assert.equal(f.nodes.find(n => n.tag === 'details').hidden, false);
    assert.equal(f.node('Copy link').hidden, false);
  });
}

test('disabled guest access hides the panel and background reconciliation can restore it', async () => {
  const f = fixture();
  f.responses.push(activeLink('/watch#old')); await mountObserverLink(f);
  f.responses.push({ ok: true, json: async () => ({ Enabled: false }) }); f.events.focus(); await tick();
  assert.equal(f.nodes.find(n => n.tag === 'details').hidden, true);
  assert.equal(f.node('Copy link').hidden, true);
});

for (const operation of ['refresh', 'copy', 'create', 'failed refresh']) {
  test(`page exit cancels ${operation}, closes messaging and prevents late UI or clipboard writes`, async () => {
    const { Channel, channels } = messaging();
    const f = fixture(Channel);
    f.responses.push(activeLink('/watch#old')); await mountObserverLink(f);
    const copied = [];
    f.window.navigator.clipboard.writeText = async value => copied.push(value);
    let release;
    if (operation === 'failed refresh') f.responses.push(new Error('offline'));
    else f.responses.push({ ok: true, json: () => new Promise(resolve => { release = resolve; }) });
    if (operation === 'copy') f.node('Copy link').handlers.click();
    else if (operation === 'create') f.node('Create / replace link').handlers.click();
    else f.events.focus();
    if (operation !== 'failed refresh') await tick();
    f.events.pagehide();
    if (release) release({ Enabled: true, HasLink: true, Url: '/watch#late' });
    await tick();
    assert.deepEqual(copied, []); assert.equal(f.timers.size, 0); assert.equal(channels[0].closed, true);
    assert.equal(f.events.focus, undefined); assert.equal(f.documentEvents.visibilitychange, undefined);
    assert.equal(f.requests.at(-1).options.signal.aborted, true);
    channels[0].receive({ roomId: f.roomId }); f.node('Create / replace link').handlers.click();
    await f.node('Copy link').handlers.click();
    let reloads = 0; f.window.location.reload = () => reloads++;
    f.events.pageshow({ persisted: false }); f.events.pageshow({ persisted: true });
    assert.equal(reloads, 1);
  });
}

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
  f.responses.push({ ok: true, json: async () => ({ Enabled: true, HasLink: true, Url: '/watch#guest-capability' }) });
  await f.node('Copy link').handlers.click(); assert(f.node('Guest link copied.'));
  assert.deepEqual(copied, ['https://test.invalid/watch#guest-capability']);
  f.window.navigator.clipboard.writeText = async () => { throw new Error('Denied'); };
  f.responses.push({ ok: true, json: async () => ({ Enabled: true, HasLink: true, Url: '/watch#guest-capability' }) });
  await f.node('Copy link').handlers.click(); assert(input.selected);
  f.responses.push({ ok: true });
  f.node('Disable guest access').handlers.click(); await tick();
  assert.equal(input.value, ''); assert.equal(input.hidden, true);
  assert.equal(copy.hidden, true); assert.equal(copy.disabled, true);
  assert.equal(disable.hidden, true); assert.equal(disable.disabled, true);
  await copy.handlers.click();
  assert(f.node('Guest access disabled.'));
});

test('a retained link can be copied after reload without rotation and revoked after a failed attempt', async () => {
  const f = fixture();
  f.responses.push({ ok: true, json: async () => ({ Enabled: true, HasLink: true, Url: '/watch#existing-capability', MaxPerRoom: 20 }) });
  await mountObserverLink(f);
  const copy = f.node('Copy link');
  const disable = f.node('Disable guest access');
  assert.equal(copy.hidden, false); assert.equal(copy.disabled, false);
  const copied = [];
  f.window.navigator.clipboard.writeText = async value => copied.push(value);
  f.responses.push({ ok: true, json: async () => ({ Enabled: true, HasLink: true, Url: '/watch#existing-capability' }) });
  await copy.handlers.click();
  assert.deepEqual(copied, ['https://test.invalid/watch#existing-capability']);
  assert.equal(f.requests.length, 2);
  assert.equal(disable.hidden, false); assert.equal(disable.disabled, false);
  f.responses.push({ ok: false });
  disable.handlers.click(); await tick();
  assert.equal(disable.hidden, false); assert.equal(disable.disabled, false);
  f.responses.push({ ok: true });
  disable.handlers.click(); await tick();
  assert.equal(disable.hidden, true); assert.equal(disable.disabled, true);
});

for (const state of [
  { Enabled: true, HasLink: false, Url: null },
  { Enabled: false, HasLink: true, Url: '/watch#old' },
  { Enabled: true, HasLink: true, Url: null },
  { Enabled: true, HasLink: true, Url: '/watch#replacement' }
]) {
  test(`copy revalidates the cached link against server state ${JSON.stringify(state)}`, async () => {
    const f = fixture();
    f.responses.push({ ok: true, json: async () => ({ Enabled: true, HasLink: true, Url: '/watch#old' }) });
    await mountObserverLink(f);
    const copied = [];
    f.window.navigator.clipboard.writeText = async value => copied.push(value);
    f.responses.push({ ok: true, json: async () => state });
    const pending = f.node('Copy link').handlers.click();
    await f.node('Copy link').handlers.click();
    await pending;
    const active = state.Enabled && state.HasLink && state.Url;
    assert.deepEqual(copied, active ? ['https://test.invalid/watch#replacement'] : []);
    assert.equal(f.nodes.find(n => n.tag === 'input').value, active ? copied[0] : '');
    assert.equal(f.node('Copy link').hidden, !active);
    assert.equal(f.requests.length, 2);
    assert.equal(f.requests[1].options.cache, 'no-store');
  });
}

for (const failure of ['unauthorized', 'network', 'invalid JSON']) {
  test(`copy never falls back to the cached link after ${failure}`, async () => {
    const f = fixture();
    f.responses.push({ ok: true, json: async () => ({ Enabled: true, HasLink: true, Url: '/watch#old' }) });
    await mountObserverLink(f);
    const copied = [];
    f.window.navigator.clipboard.writeText = async value => copied.push(value);
    if (failure === 'unauthorized') f.responses.push({ ok: false });
    if (failure === 'network') f.responses.push(new TypeError('offline'));
    if (failure === 'invalid JSON') f.responses.push({ ok: true, json: async () => { throw new SyntaxError('invalid'); } });
    await f.node('Copy link').handlers.click();
    assert.deepEqual(copied, []);
    const input = f.nodes.find(n => n.tag === 'input');
    assert.equal(input.value, ''); assert.equal(input.selected, undefined);
    assert.equal(f.node('Copy link').hidden, true);
    assert(f.node('Could not verify guest access. Retrying automatically…'));
  });
}
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
