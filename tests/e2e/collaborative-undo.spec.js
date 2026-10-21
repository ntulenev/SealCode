import { csrf } from './admin-token.js';
import { expect, test } from './fixtures.js';

async function createRoom(request) {

  const createResponse = await request.post('/admin/rooms', {
    headers: await csrf(request),
    data: {
      name: `Undo ${Date.now()}`,
      language: 'csharp'
    }
  });
  expect(createResponse.ok()).toBeTruthy();
  return (await createResponse.json()).RoomId;
}

async function joinRoom(page, roomId, name) {
  await page.goto(`/room/${roomId}`);
  await page.locator('#displayNameInput').fill(name);
  await page.locator('#joinBtn').click();
  await expect(page.locator('#joinOverlay')).toHaveClass(/hidden/);
  await expect(page.locator('#connectionStatus')).toHaveText('connected');
}

async function focusEditor(page) {
  await page.locator('#monacoEditor').click();
  await expect(page.locator('.monaco-editor textarea.inputarea')).toBeFocused();
}

async function editorText(page) {
  return page.evaluate(() => monaco.editor.getModels()[0].getValue());
}

async function expectConverged(alice, bob, predicate) {
  await expect
    .poll(async () => {
      const aliceText = await editorText(alice);
      const bobText = await editorText(bob);
      return {
        aliceText,
        bobText,
        accepted: aliceText === bobText && predicate(aliceText)
      };
    })
    .toEqual(
      expect.objectContaining({
        accepted: true,
        aliceText: expect.any(String),
        bobText: expect.any(String)
      })
    );
  expect(await editorText(alice)).toBe(await editorText(bob));
}

test('Ctrl+Z and redo affect only the current user concurrent edits', async ({
  browser,
  request
}) => {
  const roomId = await createRoom(request);
  const aliceContext = await browser.newContext();
  const bobContext = await browser.newContext();
  const alice = await aliceContext.newPage();
  const bob = await bobContext.newPage();
  await Promise.all([joinRoom(alice, roomId, 'Alice'), joinRoom(bob, roomId, 'Bob')]);

  await Promise.all([focusEditor(alice), focusEditor(bob)]);
  await Promise.all([
    alice.keyboard.type('alice-change'),
    bob.keyboard.type('bob-change')
  ]);
  await expectConverged(
    alice,
    bob,
    (text) => text.includes('alice-change') && text.includes('bob-change')
  );

  await focusEditor(alice);
  await alice.keyboard.press('Control+z');
  await expectConverged(
    alice,
    bob,
    (text) => !text.includes('alice-change') && text.includes('bob-change')
  );

  await alice.keyboard.press('Control+y');
  await expectConverged(
    alice,
    bob,
    (text) => text.includes('alice-change') && text.includes('bob-change')
  );

  await Promise.all([focusEditor(alice), focusEditor(bob)]);
  await Promise.all([alice.keyboard.press('Control+z'), bob.keyboard.press('Control+z')]);
  await expectConverged(alice, bob, (text) => text === '');

  await aliceContext.close();
  await bobContext.close();
});

test('a reconnect preserves local history and merges the server state', async ({
  browser,
  request
}) => {
  const roomId = await createRoom(request);
  const aliceContext = await browser.newContext();
  const bobContext = await browser.newContext();
  const alice = await aliceContext.newPage();
  const bob = await bobContext.newPage();
  await Promise.all([
    joinRoom(alice, roomId, 'AliceReconnect'),
    joinRoom(bob, roomId, 'BobReconnect')
  ]);

  await focusEditor(alice);
  await alice.keyboard.type('alice-local');
  await focusEditor(bob);
  await bob.keyboard.type('bob-remote');
  await expectConverged(
    alice,
    bob,
    (text) => text.includes('alice-local') && text.includes('bob-remote')
  );

  const reconnected = await alice.evaluate(() => window.__debugReconnect());
  expect(reconnected).toBeTruthy();
  await expect(alice.locator('#connectionStatus')).toHaveText('connected');

  await focusEditor(alice);
  await alice.keyboard.press('Control+z');
  await expectConverged(
    alice,
    bob,
    (text) => !text.includes('alice-local') && text.includes('bob-remote')
  );

  await aliceContext.close();
  await bobContext.close();
});
