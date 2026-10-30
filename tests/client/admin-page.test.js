import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createAdminPage } from '../../src/SealCode/wwwroot/admin.js';

function element(tag = 'div') {
  return {
    tag,
    children: [],
    handlers: {},
    value: '',
    textContent: '',
    classList: { remove() {} },
    append(...items) {
      this.children.push(...items);
    },
    appendChild(item) {
      this.children.push(item);
    },
    addEventListener(name, handler) {
      this.handlers[name] = handler;
    },
    set innerHTML(value) {
      this.children = [];
    },
    get innerHTML() {
      return '';
    }
  };
}
function find(root, text) {
  return root.textContent === text
    ? root
    : root.children.map((child) => find(child, text)).find(Boolean);
}
async function fixture({
  tokenStatus = 200,
  clipboard = async () => {},
  rooms = [],
  displayName = 'Admin',
  getRooms,
  post
} = {}) {
  const elements = Object.fromEntries(
    ['rooms', 'createRoom', 'roomName', 'roomLanguage', 'createResult', 'adminGreeting', 'myRoomsOnly']
      .map((id) => [id, element()])
  );
  elements.myRoomsOnly.checked = true;
  elements.roomName.maxLength = 160;
  const fetch = async (url, options) => {
    if (url.startsWith('/admin/rooms') && !options && getRooms) return getRooms(url);
    if (options?.method === 'POST' && post) return post();
    return {
      ok: url !== '/admin/token' || tokenStatus === 200,
      status: url === '/admin/token' ? tokenStatus : 200,
      json: async () => {
        if (url === '/admin/me') return { DisplayName: displayName };
        if (url.startsWith('/admin/rooms')) return rooms;
        if (url === '/languages') return ['csharp'];
        return { Token: 'token' };
      }
    };
  };
  const page = createAdminPage({
    document: { getElementById: (id) => elements[id], createElement: element },
    window: { location: { origin: 'https://example.com' } },
    fetch,
    confirm: () => true,
    clipboard: { writeText: clipboard }
  });
  await page.ready;
  return elements;
}
const room = { RoomId: 'id/with space', Name: null, Language: 'sql', CanDelete: true };

test('greeting renders the account name as text', async () => {
  const elements = await fixture({ displayName: '<img onerror=alert(1)>@example.com' });
  assert.equal(elements.adminGreeting.textContent, 'Hello, <img onerror=alert(1)>@example.com!');
  assert.equal(elements.adminGreeting.children.length, 0);
});

test('room filter ignores stale responses and stays active after creation', async () => {
  const requests = [];
  let finishOldRequest;
  const elements = await fixture({
    getRooms: (url) => {
      requests.push(url);
      if (requests.length === 2) {
        return new Promise((resolve) => { finishOldRequest = resolve; });
      }
      return { ok: true, json: async () => [{ ...room, Name: url }] };
    },
    post: async () => ({ ok: true, json: async () => ({ RoomId: 'new' }) })
  });
  assert.equal(requests[0], '/admin/rooms?mine=true');
  elements.myRoomsOnly.checked = false;
  const oldRequest = elements.myRoomsOnly.handlers.change();
  elements.myRoomsOnly.checked = true;
  await elements.myRoomsOnly.handlers.change();
  finishOldRequest({ ok: true, json: async () => [{ ...room, Name: 'Stale' }] });
  await oldRequest;
  assert.ok(find(elements.rooms, '/admin/rooms?mine=true'));
  assert.equal(find(elements.rooms, 'Stale'), undefined);
  elements.myRoomsOnly.checked = true;
  await elements.myRoomsOnly.handlers.change();
  elements.roomName.value = 'New room';
  elements.roomLanguage.value = 'csharp';
  await elements.createRoom.handlers.click();
  assert.equal(requests.at(-1), '/admin/rooms?mine=true');
  assert.ok(find(elements.rooms, '/admin/rooms?mine=true'));
});

test('admin token failures are displayed by the delete handler', async () => {
  const elements = await fixture({ tokenStatus: 401, rooms: [room] });
  await find(elements.rooms, 'Delete').handlers.click();
  assert.equal(elements.createResult.textContent, 'Session expired. Sign in again.');
});

test('clipboard rejection reports failure and URLs encode the room id', async () => {
  let url;
  const elements = await fixture({
    rooms: [room],
    clipboard: async (value) => {
      url = value;
      throw new Error('denied');
    }
  });
  await find(elements.rooms, 'Copy link').handlers.click();
  assert.equal(url, 'https://example.com/room/id%2Fwith%20space');
  assert.equal(elements.createResult.textContent, 'Could not copy the link.');
});

test('null and untrusted names render as text without dropping other rows', async () => {
  const elements = await fixture({
    rooms: [room, { ...room, Name: '<img onerror=alert(1)>' }]
  });
  assert.equal(elements.rooms.children.length, 2);
  const link = elements.rooms.children[0].children[0].children[0];
  assert.equal(link.tag, 'a');
  assert.equal(link.href, '/room/id%2Fwith%20space');
  assert.equal(link.target, '_blank');
  assert.equal(link.rel, 'noopener noreferrer');
  assert.equal(link.children[0].textContent, '');
  assert.ok(find(elements.rooms, '<img onerror=alert(1)>'));
  assert.equal(find(elements.rooms, '<img onerror=alert(1)>').title, '<img onerror=alert(1)>');
});

test('oversized room names are rejected before a request is sent', async () => {
  let posts = 0;
  const elements = await fixture({ post: () => { posts++; } });
  elements.roomName.value = 'я'.repeat(161);
  elements.roomLanguage.value = 'csharp';
  await elements.createRoom.handlers.click();
  assert.equal(posts, 0);
  assert.equal(elements.createResult.textContent, 'Room name must be at most 160 characters.');
  assert.equal(elements.createRoom.disabled, false);
});

test('create ignores a second click while the first request is pending', async () => {
  let finish;
  let posts = 0;
  const elements = await fixture({
    post: () => {
      posts++;
      return new Promise((resolve) => {
        finish = resolve;
      });
    }
  });
  elements.roomName.value = 'Room';
  elements.roomLanguage.value = 'csharp';
  const first = elements.createRoom.handlers.click();
  await elements.createRoom.handlers.click();
  await new Promise(setImmediate);
  assert.equal(posts, 1);
  assert.equal(elements.createRoom.disabled, true);
  finish({ ok: true, json: async () => ({ RoomId: 'id' }) });
  await first;
  assert.equal(elements.createRoom.disabled, false);
});
