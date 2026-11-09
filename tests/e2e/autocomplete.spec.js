import { test, expect } from './fixtures.js';
import { csrf } from './admin-token.js';

async function createRoom(request, language = 'javascript') {
  const response = await request.post('/admin/rooms', {
    headers: await csrf(request), data: { name: 'Autocomplete', language }
  });
  expect(response.ok()).toBeTruthy();
  return `/room/${(await response.json()).RoomId}`;
}

async function join(page, path, name) {
  await page.goto(path);
  await page.locator('#displayNameInput').fill(name);
  await page.locator('#joinBtn').click();
  await expect(page.locator('#joinOverlay')).toBeHidden();
}

async function focusEnd(page) {
  await page.evaluate(() => monaco.editor.getEditors()[0].focus());
  await page.keyboard.press('Control+End');
}

async function expectText(pages, text) {
  for (const page of pages) {
    await expect.poll(() => page.evaluate(() =>
      monaco.editor.getModels()[0].getValue().replaceAll('\r\n', '\n')
    )).toBe(text);
    await expect.poll(() => page.locator('#monacoEditor .view-lines .view-line').evaluateAll(
      (lines) => lines.sort((a, b) => parseFloat(a.style.top) - parseFloat(b.style.top))
        .map((line) => line.textContent.replaceAll('\u00a0', ' ')).join('\n')
    )).toBe(text);
  }
}

const suggestions = (page) => page.locator('.suggest-widget.visible');

test('C# automatically completes identifiers already in the room and supports manual completion', async ({ page, request }, testInfo) => {
  await join(page, await createRoom(request, 'csharp'), 'CSharp user');
  expect(await page.evaluate(() => monaco.editor.getModels()[0].getLanguageId())).toBe('csharp');
  const seed = 'class CustomerService { public void ProcessOrder() {} }\n';
  await focusEnd(page);
  await page.keyboard.insertText(seed);
  await page.keyboard.type('CustomerS');
  const item = suggestions(page).locator('.monaco-list-row').filter({
    has: page.locator('.label-name', { hasText: /^CustomerService$/ })
  });
  await expect(item).toBeVisible();
  await expect(item).toHaveClass(/focused/);
  await page.screenshot({ path: testInfo.outputPath('csharp-word-completion.png') });
  await page.keyboard.press('Tab');
  await expectText([page], seed + 'CustomerService');
  await focusEnd(page);
  await page.keyboard.insertText('\n');
  await page.keyboard.type('ProcessO');
  await expect(suggestions(page)).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(suggestions(page)).toBeHidden();
  await page.keyboard.press('Control+Space');
  const method = suggestions(page).locator('.monaco-list-row').filter({
    has: page.locator('.label-name', { hasText: /^ProcessOrder$/ })
  });
  await expect(method).toBeVisible();
  await expect(method).toHaveClass(/focused/);
  await page.keyboard.press('Enter');
  await expectText([page], seed + 'CustomerService\nProcessOrder');
  await focusEnd(page);
  await page.keyboard.insertText('\n');
  await page.keyboard.type('CustomerS');
  await expect(item).toBeVisible();
});

for (const input of ['pub', 'Console.Wri']) {
  test(`built-in C# support has no semantic or keyword suggestions for ${input} in a fresh document`, async ({ page, request }) => {
    await join(page, await createRoom(request, 'csharp'), 'CSharp user');
    await focusEnd(page);
    await page.keyboard.type(input);
    await page.keyboard.press('Control+Space');
    // Wait for Monaco's completed response instead of asserting absence before
    // the language worker has had a chance to answer.
    await expect(suggestions(page).locator('.message')).toHaveText('No suggestions.');
    await expect(suggestions(page).locator('.monaco-list-row')).toHaveCount(0);
  });
}

// Hold real encoded Yjs updates before they reach the server. Both editors stay
// connected and editable, but neither can observe the other's concurrent edit.
async function gateUpdates(page) {
  let holding = false;
  const pending = [];
  await page.routeWebSocket('**/roomHub*', (route) => {
    const server = route.connectToServer();
    route.onMessage((message) => {
      if (typeof message !== 'string') return server.send(message);
      for (const record of message.split('\x1e').filter(Boolean)) {
        if (holding && JSON.parse(record).target === 'UpdateYjs')
          pending.push(() => server.send(record + '\x1e'));
        else server.send(record + '\x1e');
      }
    });
  });
  return {
    hold() { holding = true; },
    count: () => pending.length,
    release() {
      holding = false;
      for (const send of pending.splice(0)) send();
    }
  };
}

const overlappingEdits = [
  { name: 'insert inside prefix', start: 4, end: 4, text: 'REMOTE', local: 'shaREMOTEredV',
    undoCompletion: 'shaREMOTEredV', undoRemote: 'sharedValue', undoBoth: 'sharedV' },
  { name: 'replace entire prefix', start: 1, end: 8, text: 'REMOTE', local: 'REMOTE',
    undoCompletion: 'sharedVREMOTE', undoRemote: 'sharedVsharedValue', undoBoth: 'sharedVsharedV' },
  { name: 'delete entire prefix', start: 1, end: 8, text: '', local: '',
    undoCompletion: 'sharedV', undoRemote: 'sharedVsharedValue', undoBoth: 'sharedVsharedV' }
];

for (const edit of overlappingEdits) {
  for (const first of ['completion', 'remote edit']) {
    test(`concurrent ${edit.name}: ${first} delivered first converges through undo and redo`, async ({ page: alice, browser, request }) => {
      const path = await createRoom(request);
      const bobContext = await browser.newContext();
      try {
        const bob = await bobContext.newPage();
        const aliceGate = await gateUpdates(alice);
        const bobGate = await gateUpdates(bob);
        await join(alice, path, 'Alice');
        await join(bob, path, 'Bob');
        const seed = 'const sharedValue = 42;\n';
        const baseline = seed + 'sharedV';
        await focusEnd(bob);
        await bob.keyboard.insertText(baseline);
        await expectText([alice, bob], baseline);
        await focusEnd(alice);
        await alice.keyboard.press('Control+Space');
        const item = suggestions(alice).locator('.monaco-list-row').filter({
          has: alice.locator('.label-name', { hasText: /^sharedValue$/ })
        });
        await expect(item).toBeVisible();
        await expect(item).toHaveClass(/focused/);
        // Explicit movement also separates Bob's seed from his conflicting edit.
        await focusEnd(bob);
        await bob.keyboard.press('ArrowLeft');
        await bob.keyboard.press('ArrowRight');
        await bob.evaluate(({ start, end }) => {
          monaco.editor.getEditors()[0].setSelection(new monaco.Range(2, start, 2, end));
        }, edit);
        const revision = await alice.evaluate(() => window.__debugUndoState().documentRevision);
        aliceGate.hold();
        bobGate.hold();
        await alice.keyboard.press(first === 'completion' ? 'Enter' : 'Tab');
        if (edit.text) await bob.keyboard.insertText(edit.text);
        else await bob.keyboard.press('Backspace');
        await expect.poll(aliceGate.count).toBe(1);
        await expect.poll(bobGate.count).toBe(1);
        // These divergent local states prove that the operations are concurrent,
        // rather than two edits that happened to run close together in time.
        await expectText([alice], seed + 'sharedValue');
        await expectText([bob], seed + edit.local);
        expect(await alice.evaluate(() => window.__debugUndoState().documentRevision)).toBe(revision);
        expect(await bob.evaluate(() => window.__debugUndoState().documentRevision)).toBe(revision);
        const gates = first === 'completion' ? [aliceGate, bobGate] : [bobGate, aliceGate];
        gates[0].release();
        await expect.poll(() => alice.evaluate(() => window.__debugUndoState().documentRevision)).toBe(revision + 1);
        gates[1].release();
        for (const page of [alice, bob])
          await expect.poll(() => page.evaluate(() => window.__debugUndoState().documentRevision)).toBe(revision + 2);
        const merged = await alice.evaluate(() => monaco.editor.getModels()[0].getValue().replaceAll('\r\n', '\n'));
        // Yjs may order concurrent insertions either way according to client IDs.
        expect([seed + 'sharedValue' + edit.text, seed + edit.text + 'sharedValue']).toContain(merged);
        await expectText([alice, bob], merged);
        await expect(suggestions(alice)).toBeHidden();
        await focusEnd(alice);
        await alice.keyboard.press('Control+z');
        // Yjs undo restores a deleted prefix even if the other participant also
        // deleted those same CRDT items. Undoing both replacements can therefore
        // restore the prefix twice. Keep this limitation explicit, rather than
        // claiming that overlapping deletions have independent undo semantics.
        await expectText([alice, bob], seed + edit.undoCompletion);
        await alice.keyboard.press('Control+y');
        await expectText([alice, bob], merged);
        await focusEnd(bob);
        await bob.keyboard.press('Control+z');
        await expectText([alice, bob], seed + edit.undoRemote);
        await focusEnd(alice);
        await alice.keyboard.press('Control+z');
        await expectText([alice, bob], seed + edit.undoBoth);
        expect(await bob.evaluate(() => window.__debugReconnect())).toBe(true);
        await expectText([alice, bob], seed + edit.undoBoth);
        for (const page of [alice, bob]) {
          await expect(page.locator('#connectionStatus')).toHaveText('connected');
          await expect(page.locator('#syncError')).toBeHidden();
        }
      } finally { await bobContext.close(); }
    });
  }
}

for (const storage of ['previous opt-out', 'blocked']) {
  test(`autocomplete stays enabled with ${storage} browser storage and has no toggle`, async ({ page, request }) => {
    await page.addInitScript((storage) => {
      if (storage === 'previous opt-out') localStorage.setItem('sealcode.autocomplete', 'false');
      else Object.defineProperty(window, 'localStorage', {
        get() { throw new DOMException('Blocked', 'SecurityError'); }
      });
    }, storage);
    const path = await createRoom(request);
    await join(page, path, 'Alice');
    await expect(page.getByRole('checkbox', { name: 'Autocomplete' })).toHaveCount(0);
    await focusEnd(page);
    await page.keyboard.type('cons');
    await expect(suggestions(page)).toBeVisible();
    await join(page, await createRoom(request), 'Alice');
    await focusEnd(page);
    await page.keyboard.type('cons');
    await expect(suggestions(page)).toBeVisible();
    await page.reload();
    await page.locator('#displayNameInput').fill('Alice');
    await page.locator('#joinBtn').click();
    await expect(page.locator('#joinOverlay')).toBeHidden();
    await focusEnd(page);
    await page.keyboard.insertText('\n');
    await page.keyboard.type('cons');
    await expect(suggestions(page)).toBeVisible();
  });
}

for (const acceptKey of ['Enter', 'Tab']) {
  test(`native completion accepted with ${acceptKey} syncs, undoes and redoes with remote edits`, async ({ page: alice, browser, request }) => {
    const path = await createRoom(request);
    const bobContext = await browser.newContext();
    try {
      const bob = await bobContext.newPage();
      await join(alice, path, 'Alice');
      await join(bob, path, 'Bob');
      const seed = 'const sharedValue = 42;\n';
      await focusEnd(bob);
      await bob.keyboard.insertText(seed);
      await expectText([alice, bob], seed);
      await focusEnd(alice);
      await alice.keyboard.type('sharedV');
      await expect(suggestions(alice)).toBeVisible();
      // Explicit cursor movement separates the prefix from the completion's undo group.
      await alice.keyboard.press('Escape');
      await alice.keyboard.press('ArrowLeft');
      await alice.keyboard.press('ArrowRight');
      await alice.keyboard.press('Control+Space');
      const item = suggestions(alice).locator('.monaco-list-row').filter({
        has: alice.locator('.label-name', { hasText: /^sharedValue$/ })
      });
      await expect(item).toBeVisible();
      await expect(item).toHaveClass(/focused/);
      await alice.keyboard.press(acceptKey);
      await expectText([alice, bob], seed + 'sharedValue');
      await expect(suggestions(alice)).toBeHidden();

      // The second participant edits after completion, before Alice undoes it.
      await focusEnd(bob);
      await bob.keyboard.insertText('; // Bob');
      await expectText([alice, bob], seed + 'sharedValue; // Bob');
      await focusEnd(alice);
      await alice.keyboard.press('Control+z');
      await expectText([alice, bob], seed + 'sharedV; // Bob');
      await alice.keyboard.press('Control+y');
      await expectText([alice, bob], seed + 'sharedValue; // Bob');

      expect(await bob.evaluate(() => window.__debugReconnect())).toBe(true);
      await expectText([alice, bob], seed + 'sharedValue; // Bob');
      await focusEnd(bob);
      await bob.keyboard.press('Control+z');
      await expectText([alice, bob], seed + 'sharedValue');
      await focusEnd(alice);
      await alice.keyboard.press('Control+z');
      await expectText([alice, bob], seed + 'sharedV');
      await alice.keyboard.press('Control+z');
      await expectText([alice, bob], seed);
    } finally { await bobContext.close(); }
  });
}
