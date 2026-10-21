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
  rooms = []
} = {}) {
  const elements = Object.fromEntries(
    ['rooms', 'createRoom', 'roomName', 'roomLanguage', 'createResult'].map((id) => [
      id,
      element()
    ])
  );
  const fetch = async (url) => ({
    ok: url !== '/admin/token' || tokenStatus === 200,
    status: url === '/admin/token' ? tokenStatus : 200,
    json: async () =>
      url === '/admin/rooms'
        ? rooms
        : url === '/languages'
          ? ['csharp']
          : { Token: 'token' }
  });
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
  assert.equal(elements.rooms.children[0].children[0].children[0].textContent, '');
  assert.ok(find(elements.rooms, '<img onerror=alert(1)>'));
});
