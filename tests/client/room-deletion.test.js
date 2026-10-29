import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createRoomDeletion } from '../../src/SealCode/wwwroot/room-deletion.js';

function element() {
  const classes = new Set(['hidden']);
  let onClick;
  return {
    disabled: false,
    textContent: '',
    classList: {
      add: (name) => classes.add(name),
      remove: (name) => classes.delete(name),
      contains: (name) => classes.has(name)
    },
    addEventListener: (event, handler) => {
      if (event === 'click') onClick = handler;
    },
    click: () => onClick()
  };
}

function fixture({ permission = true, confirm = true, deleteStatus = 200, tokenStatus = 200 } = {}) {
  const button = element();
  const error = element();
  const calls = [];
  const destinations = [];
  let closed = false;
  const fetch = async (url, options = {}) => {
    calls.push({ url, options });
    if (url === '/admin/token')
      return { ok: tokenStatus === 200, status: tokenStatus, json: async () => ({ Token: 'csrf' }) };
    if (options.method === 'DELETE') return { ok: deleteStatus === 200, status: deleteStatus };
    return { ok: permission !== null, status: permission === null ? 401 : 200,
      json: async () => ({ CanDelete: permission }) };
  };
  const controller = createRoomDeletion({
    button,
    error,
    roomId: 'id/with space',
    window: { confirm: () => confirm, location: { assign: (path) => destinations.push(path) } },
    fetch,
    isClosed: () => closed
  });
  return { button, error, calls, destinations, controller, close: () => { closed = true; controller.hide(); } };
}

test('only an authorized admin sees deletion; confirmation and CSRF protect the action', async () => {
  const canceled = fixture({ confirm: false });
  await canceled.controller.refresh();
  assert.equal(canceled.button.classList.contains('hidden'), false);
  assert.equal(canceled.calls[0].url, '/admin/rooms/id%2Fwith%20space');
  assert.equal(canceled.calls[0].options.cache, 'no-store');
  await canceled.button.click();
  assert.equal(canceled.calls.length, 1);

  const accepted = fixture();
  await accepted.controller.refresh();
  await accepted.button.click();
  assert.equal(accepted.calls[1].url, '/admin/token');
  assert.equal(accepted.calls[2].options.method, 'DELETE');
  assert.equal(accepted.calls[2].options.headers['X-CSRF-TOKEN'], 'csrf');
  assert.deepEqual(accepted.destinations, ['/']);
  assert.equal(accepted.button.disabled, false);

  for (const permission of [false, null]) {
    const denied = fixture({ permission });
    await denied.controller.refresh();
    assert.equal(denied.button.classList.contains('hidden'), true);
  }
});

test('permission changes and expired sessions keep the user in the room', async () => {
  for (const [options, message, shouldHide] of [
    [{ deleteStatus: 403 }, 'You no longer have permission', true],
    [{ deleteStatus: 401 }, 'Admin session expired', true],
    [{ tokenStatus: 401 }, 'Admin session expired', false],
    [{ deleteStatus: 500 }, 'Could not delete the room', false]
  ]) {
    const room = fixture(options);
    await room.controller.refresh();
    await room.button.click();
    assert.match(room.error.textContent, new RegExp(message));
    assert.equal(room.error.classList.contains('hidden'), false);
    assert.equal(room.button.classList.contains('hidden'), shouldHide);
    assert.deepEqual(room.destinations, []);
    assert.equal(room.button.disabled, false);
  }
});

test('a late permission response cannot show deletion after room closure', async () => {
  let resolvePermission;
  const button = element();
  const controller = createRoomDeletion({
    button,
    error: element(),
    roomId: 'room',
    window: { confirm: () => true, location: { assign() {} } },
    fetch: () => new Promise((resolve) => { resolvePermission = resolve; }),
    isClosed: () => closed
  });
  let closed = false;
  const pending = controller.refresh();
  closed = true;
  controller.hide();
  resolvePermission({ ok: true, json: async () => ({ CanDelete: true }) });
  await pending;
  await button.click();
  assert.equal(button.classList.contains('hidden'), true);
});
