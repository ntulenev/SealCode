import { expect, test } from '@playwright/test';

const ALICE_LINES = ['alice line one', 'alice line two', 'alice line three'];
const BOB_LINES = ['bob line one', 'bob line two', 'bob line three'];
const AFTER_TYPING = [...ALICE_LINES, ...BOB_LINES].join('\n');
const AFTER_ALICE_UNDO = '\n' + BOB_LINES.join('\n');

async function joinRoom(page, roomUrl, name) {
  await page.goto(roomUrl);
  await page.locator('#displayNameInput').fill(name);
  await page.locator('#joinBtn').click();
  await expect(page.locator('#joinOverlay')).toHaveClass(/hidden/);
  await expect(page.locator('#connectionStatus')).toHaveText('connected');
}

async function modelText(page) {
  // Monaco uses platform-specific line endings; compare the logical lines.
  const text = await page.evaluate(() => monaco.editor.getModels()[0].getValue());
  return text.replace(/\r\n/g, '\n');
}

async function renderedText(page) {
  return page.locator('#monacoEditor .view-lines .view-line').evaluateAll((lines) =>
    lines
      .sort((a, b) => parseFloat(a.style.top) - parseFloat(b.style.top))
      .map((line) => line.textContent.replace(/\u00a0/g, ' '))
      .join('\n')
  );
}

async function participants(page) {
  return page.locator('#participantsList li').evaluateAll((items) =>
    items.map((item) => item.getAttribute('title')).sort()
  );
}

async function expectRoomState(pages, expected) {
  for (const page of pages) {
    await expect.poll(() => modelText(page), { message: 'Monaco model text' })
      .toBe(expected.text);
    await expect.poll(() => renderedText(page), { message: 'Rendered editor text' })
      .toBe(expected.text);
    await expect.poll(() => page.locator('#languageSelect').inputValue())
      .toBe(expected.language);
    await expect.poll(() => page.evaluate(() =>
      monaco.editor.getModels()[0].getLanguageId()
    )).toBe(expected.language);
    await expect.poll(() => participants(page), { message: 'Room participants' })
      .toEqual(expected.participants);
    await expect.poll(() => page.locator('#syncError').getAttribute('class'))
      .toMatch(/\bhidden\b/);
  }
  await expect.poll(async () => {
    const versions = [];
    for (const page of pages) {
      versions.push(await page.locator('#versionNumber').innerText());
    }
    return new Set(versions).size;
  }, { message: 'All participants have the same revision' }).toBe(1);
}

async function undoAll(page) {
  await page.locator('#monacoEditor').click();
  const depth = await page.evaluate(() => window.__debugUndoState().undoDepth);
  for (let i = 0; i < depth; i += 1) {
    await page.keyboard.press('Control+z');
  }
  await expect.poll(() => page.evaluate(() => window.__debugUndoState().undoDepth))
    .toBe(0);
}

async function typeLines(page, lines) {
  for (let i = 0; i < lines.length; i += 1) {
    if (i > 0) {
      // This scenario inserts new lines; a word suggestion can otherwise consume Enter.
      await page.keyboard.press('Escape');
      await page.keyboard.press('Enter');
    }
    await page.keyboard.type(lines[i]);
  }
}

test('basic room workflow', { tag: '@smoke' }, async ({ browser }) => {
  test.setTimeout(90_000);
  const contexts = new Set();
  const errors = [];
  async function newPage(name) {
    const context = await browser.newContext();
    contexts.add(context);
    const page = await context.newPage();
    if (name !== 'Admin') {
      page.on('pageerror', (error) => errors.push(`${name}: ${error.message}`));
      page.on('console', (message) => {
        if (message.type() === 'error') errors.push(`${name}: ${message.text()}`);
      });
    }
    return page;
  }
  async function closeContext(page) {
    await page.context().close();
    contexts.delete(page.context());
  }

  try {
    const admin = await newPage('Admin');
    const alice = await newPage('Alice');
    const bob = await newPage('Bob');
    const carol = await newPage('Carol');
    let roomUrl;
    let roomId;
    const pair = { language: 'csharp', participants: ['Alice', 'Bob'] };
    const roomRow = () => admin.locator('#rooms .room-row').filter({ hasText: roomId });

    await test.step('1. Application started', async () => {
      expect((await admin.request.get('/health')).status()).toBe(200);
      await admin.goto('/');
      await expect(admin.locator('[data-app-version]')).toBeVisible();
      await expect(admin.locator('[data-app-version]')).not.toContainText('version unavailable');
    });

    await test.step('2. Admin signs in and creates a room', async () => {
      await admin.goto('/admin/login');
      await admin.locator('#name').fill('BrowserAdmin');
      await admin.locator('#password').fill('browser-pass');
      await admin.locator('button[type=submit]').click();
      await expect(admin).toHaveURL(/\/admin$/);
      await expect(admin.locator('#error')).not.toBeVisible();
      await expect(admin.locator('#roomLanguage')).toBeEnabled();
      await admin.locator('#roomLanguage').selectOption('csharp');
      const roomName = `Smoke ${Date.now() % 1_000_000}`;
      await admin.locator('#roomName').fill(roomName);
      await admin.locator('#createRoom').click();
      await expect(admin.locator('#createResult')).toContainText('Room created: ');
      roomUrl = (await admin.locator('#createResult').innerText()).replace('Room created: ', '').trim();
      const match = roomUrl.match(/\/room\/([A-Za-z0-9_-]{22})/);
      expect(match, 'Created room URL contains a room ID').not.toBeNull();
      roomId = match[1];
      await expect(roomRow()).toContainText(roomName);
      await expect(roomRow()).toContainText(roomId);
    });

    await test.step('3. Alice and Bob join the room', async () => {
      await joinRoom(alice, roomUrl, 'Alice');
      await joinRoom(bob, roomUrl, 'Bob');
      await expectRoomState([alice, bob], { ...pair, text: '' });
    });

    await test.step('4. Participants type lines in turn', async () => {
      await alice.locator('#monacoEditor').click();
      await typeLines(alice, ALICE_LINES);
      await expectRoomState([alice, bob], { ...pair, text: ALICE_LINES.join('\n') });
      await bob.locator('#monacoEditor').click();
      await bob.keyboard.press('Control+End');
      await bob.keyboard.press('Enter');
      await typeLines(bob, BOB_LINES);
      await expectRoomState([alice, bob], { ...pair, text: AFTER_TYPING });
    });

    await test.step('5. Language changes for both participants', async () => {
      await alice.locator('#languageSelect').selectOption('sql');
      pair.language = 'sql';
      await expectRoomState([alice, bob], { ...pair, text: AFTER_TYPING });
    });

    await test.step('6. A new participant receives the server state', async () => {
      await joinRoom(carol, roomUrl, 'Carol');
      await expectRoomState([alice, bob, carol], {
        text: AFTER_TYPING, language: 'sql', participants: ['Alice', 'Bob', 'Carol']
      });
      await closeContext(carol);
      await expectRoomState([alice, bob], { ...pair, text: AFTER_TYPING });
    });

    await test.step('7. Participants undo only their own changes', async () => {
      await undoAll(alice);
      await expectRoomState([alice, bob], { ...pair, text: AFTER_ALICE_UNDO });
      await undoAll(bob);
      await expectRoomState([alice, bob], { ...pair, text: '' });
    });

    await test.step('8. Leaving removes presence and allows continued editing', async () => {
      await bob.locator('#monacoEditor').click();
      await bob.keyboard.press('Control+Home');
      await alice.locator('#monacoEditor').click();
      await alice.keyboard.type('bye');
      await expect.poll(() => modelText(bob)).toBe('bye');
      const remoteCarets = bob.locator('#monacoEditor [class*="remote-caret-"]');
      await expect.poll(() => remoteCarets.count()).toBeGreaterThan(0);
      await closeContext(alice);
      await expect.poll(() => participants(bob)).toEqual(['Bob']);
      await expect.poll(() => remoteCarets.count()).toBe(0);
      await expect.poll(() => bob.locator('#connectionStatus').innerText()).toBe('connected');
      await expectRoomState([bob], { text: 'bye', language: 'sql', participants: ['Bob'] });
      await bob.locator('#monacoEditor').click();
      await bob.keyboard.press('Control+End');
      await bob.keyboard.type(' ok');
      await expect.poll(() => modelText(bob)).toBe('bye ok');
      await admin.goto('/admin');
      await expect(roomRow()).toContainText('1 users');
    });

    await test.step('9. Admin deletes the room', async () => {
      admin.once('dialog', (dialog) => dialog.accept());
      await roomRow().getByRole('button', { name: 'Delete', exact: true }).click();
      await expect(admin.locator('#createResult')).toHaveText('Room deleted.');
      await expect(roomRow()).toHaveCount(0);
      await expect(bob.locator('#joinOverlay')).not.toHaveClass(/hidden/);
      await expect(bob.locator('#joinError')).toBeVisible();
      await expect(bob.locator('#joinError')).toContainText('Room deleted by admin');
    });

    expect(errors, 'No browser errors for Alice, Bob or Carol').toEqual([]);
  } finally {
    await Promise.all([...contexts].map((context) => context.close()));
  }
});
