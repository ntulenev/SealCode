import { csrf } from './admin-token.js';
import { expect, test } from './fixtures.js';

async function createRoom(page) {
  const response = await page.request.post('/admin/rooms', {
    headers: await csrf(page.request),
    data: { name: 'Presence', language: 'csharp' }
  });
  expect(response.ok()).toBeTruthy();
  return (await response.json()).RoomId;
}

async function join(page, roomId, name) {
  await page.goto(`/room/${roomId}`);
  await page.locator('#displayNameInput').fill(name);
  await page.locator('#joinBtn').click();
  await expect(page.locator('#joinOverlay')).toHaveClass(/hidden/);
}

test('selection changes broadcast only transitions and mark both participants', async ({
  page,
  context
}) => {
  const roomId = await createRoom(page);
  const sent = [];
  page.on('websocket', (socket) =>
    socket.on('framesent', ({ payload }) => {
      if (typeof payload !== 'string') return;
      for (const record of payload.split('\x1e').filter(Boolean)) {
        const frame = JSON.parse(record);
        if (frame.target === 'UpdateSelection') sent.push(frame.arguments[1]);
      }
    })
  );
  await join(page, roomId, 'Alice');
  const observer = await context.newPage();
  await join(observer, roomId, 'Bob');
  await page.evaluate(() => {
    const editor = monaco.editor.getEditors()[0];
    editor.getModel().setValue('one\ntwo\nthree');
    editor.setSelection(new monaco.Selection(1, 1, 2, 2));
  });
  await expect.poll(() => sent.filter(Boolean).length).toBe(1);
  for (const participant of [page, observer])
    await expect(participant.locator('#participantsList li[title="Alice"]')).toHaveClass(
      /copied/
    );
  await page.evaluate(() => {
    const editor = monaco.editor.getEditors()[0];
    editor.setSelection(new monaco.Selection(1, 1, 3, 2));
    editor.setSelection(new monaco.Selection(1, 1, 2, 3));
  });
  // page.clock breaks Monaco 0.47 keyboard timing (missing event duration).
  await page.waitForTimeout(250);
  expect(sent.filter(Boolean)).toHaveLength(1);
  await page.evaluate(() =>
    monaco.editor.getEditors()[0].setPosition({ lineNumber: 1, column: 1 })
  );
  for (const participant of [page, observer])
    await expect(
      participant.locator('#participantsList li[title="Alice"]')
    ).not.toHaveClass(/copied/);
  const beforeTyping = sent.length;
  await page.locator('#monacoEditor').click();
  await page.keyboard.type('typing');
  // page.clock breaks Monaco 0.47 keyboard timing (missing event duration).
  await page.waitForTimeout(250);
  expect(sent.length).toBe(beforeTyping);
  await observer.close();
});
