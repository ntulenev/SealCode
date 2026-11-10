import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { mkdirSync, mkdtempSync, writeFileSync, rmSync, cpSync } from 'node:fs';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import { basename, dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium, expect } from '@playwright/test';

// Capture the real application using disposable local accounts and room data.
// Never reads or changes the developer's appsettings.json or running server.
// --whiteboard-only refreshes just the three board images; --skip-build uses
// existing server/client builds when a local app has locked the server executable.
// --cursors-only captures two extra board images with live participant cursors.
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const images = join(root, 'docs', 'images');
const temporaryRoot = mkdtempSync(join(tmpdir(), 'sealcode-readme-'));
const desktop = { width: 1440, height: 900 };
const mobile = { width: 390, height: 844 };
const cursorsOnly = process.argv.includes('--cursors-only');
const whiteboardOnly = cursorsOnly || process.argv.includes('--whiteboard-only');
const skipBuild = process.argv.includes('--skip-build');
let server;
let browser;

async function run(command, args) {
  const child = spawn(command, args, { cwd: root, stdio: 'inherit', windowsHide: true });
  const [code] = await once(child, 'exit');
  if (code !== 0) throw new Error(`${command} exited with ${code}`);
}

async function screenshot(page, path) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.locator('img').evaluateAll((images) => Promise.all(images.map((image) => image.decode())));
  await page.screenshot({ path, fullPage: true, animations: 'disabled' });
  console.log(`Captured ${path.slice(root.length + 1)}`);
}

async function setTheme(page, theme) {
  if (await page.locator('html').getAttribute('data-theme') !== theme)
    await page.locator('.site-header [data-theme-toggle]').click();
  await expect(page.locator('html')).toHaveAttribute('data-theme', theme);
}

async function csrf(request) {
  const response = await request.get('/admin/token');
  if (!response.ok()) throw new Error('Cannot obtain demo CSRF token');
  return { 'X-CSRF-TOKEN': (await response.json()).Token };
}

try {
  mkdirSync(images, { recursive: true });
  if (!skipBuild) {
    await run(process.execPath, ['scripts/build-client.mjs']);
    const buildArgs = ['build', 'src/SealCode/SealCode.csproj', '-c', 'Debug', '-p:SkipClientBuild=true'];
    if (process.env.SCREENSHOT_VERSION)
      buildArgs.push(`-p:MinVerVersionOverride=${process.env.SCREENSHOT_VERSION}`, '-p:IncludeSourceRevisionInInformationalVersion=false');
    await run('dotnet', buildArgs);
  }

  const listener = createServer();
  listener.listen(0, '127.0.0.1');
  await once(listener, 'listening');
  const port = listener.address().port;
  await new Promise((done) => listener.close(done));
  const baseURL = `http://127.0.0.1:${port}`;
  writeFileSync(join(temporaryRoot, 'appsettings.json'), JSON.stringify({
    AdminUsers: [{ Name: 'DemoAdmin', Password: 'local-screenshot-demo', IsSuperAdmin: true }],
    Languages: ['csharp', 'sql', 'javascript'],
    MaxUsersPerRoom: 5,
    Snippets: { Enabled: true, SuperAdminsOnly: true },
    Logging: { LogLevel: { Default: 'Warning' } }
  }));
  cpSync(join(root, 'src/SealCode/Resources/Snippets'), join(temporaryRoot, 'Resources/Snippets'), { recursive: true });
  server = spawn('dotnet', [
    join(root, 'src/SealCode/bin/Debug/net10.0/SealCode.dll'),
    '--contentRoot', temporaryRoot,
    '--webroot', join(root, 'src/SealCode/wwwroot'),
    '--urls', baseURL
  ], { cwd: root, windowsHide: true, env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Development' }, stdio: ['ignore', 'pipe', 'pipe'] });
  let serverLog = '';
  server.stdout.on('data', (chunk) => { serverLog = (serverLog + chunk).slice(-10000); });
  server.stderr.on('data', (chunk) => { serverLog = (serverLog + chunk).slice(-10000); });
  server.on('error', (error) => { serverLog += error.message; });
  await expect.poll(async () => {
    if (server.exitCode !== null) throw new Error(serverLog);
    try { return (await fetch(`${baseURL}/health`, { signal: AbortSignal.timeout(1000) })).ok; }
    catch { return false; }
  }, { timeout: 30000, message: 'Start isolated screenshot server' }).toBe(true);

  browser = await chromium.launch({ headless: true });
  const errors = [];
  async function newPage() {
    const context = await browser.newContext({ baseURL, viewport: desktop, deviceScaleFactor: 1, permissions: ['clipboard-read', 'clipboard-write'] });
    const page = await context.newPage();
    page.on('pageerror', (error) => errors.push(error.message));
    return page;
  }
  const admin = await newPage();
  await admin.goto('/admin/login');
  if (!whiteboardOnly) await screenshot(admin, join(images, 'admin-login.png'));
  await admin.locator('#name').fill('DemoAdmin');
  await admin.locator('#password').fill('local-screenshot-demo');
  await admin.locator('button[type="submit"]').click();
  await expect(admin).toHaveURL(`${baseURL}/admin`);

  if (!whiteboardOnly) {
    let roomPath;
    for (const [name, language] of [['Friday pairing', 'javascript'], ['SQL workshop', 'sql'], ['C# interview', 'csharp']]) {
      const response = await admin.request.post('/admin/rooms', {
        headers: await csrf(admin.request), data: { name, language }
      });
      expect(response.ok()).toBe(true);
      const room = await response.json();
      if (!roomPath) roomPath = `/room/${room.RoomId}`;
    }

    const owner = await newPage();
    await owner.context().addCookies(await admin.context().cookies());
    await owner.goto('/');
    await screenshot(owner, join(images, 'home-dark.png'));
    await setTheme(owner, 'light');
    await screenshot(owner, join(images, 'home-light.png'));
    await owner.setViewportSize(mobile);
    await screenshot(owner, join(images, 'home-mobile.png'));
    await owner.setViewportSize(desktop);
    await setTheme(owner, 'dark');
    await owner.goto(roomPath);
    await owner.locator('#displayNameInput').fill('Nikita');
    await screenshot(owner, join(images, 'join-room.png'));
    await owner.locator('#joinBtn').click();
    await expect(owner.locator('#joinOverlay')).toBeHidden();
    await expect(owner.locator('#connectionStatus')).toHaveText('connected');

    const code = [
      '// A shared space for good ideas.',
      'const teammates = ["Nikita", "Alex", "Maya", "Sam"];',
      '',
      'function createSession(name, language = "javascript") {',
      '  return {',
      '    name,',
      '    language,',
      '    participants: [],',
      '',
      '    addParticipant(displayName) {',
      '      if (!this.participants.includes(displayName)) {',
      '        this.participants.push(displayName);',
      '      }',
      '    }',
      '  };',
      '}',
      '',
      'const session = createSession("Friday pairing");',
      'teammates.forEach(name => session.addParticipant(name));',
      '',
      '// One document. Everyone on the same page.',
      'console.log(session.participants);',
      ''
    ].join('\n');
    await owner.evaluate(() => monaco.editor.getEditors()[0].focus());
    // Paste preserves the sample's indentation; typing multiline text adds auto-indent.
    await owner.evaluate((text) => navigator.clipboard.writeText(text), code);
    await owner.keyboard.press('Control+v');

    const peers = {};
    for (const name of ['Alex', 'Maya', 'Sam']) {
      const page = await newPage();
      await page.goto(roomPath);
      await page.locator('#displayNameInput').fill(name);
      await page.locator('#joinBtn').click();
      await expect(page.locator('#joinOverlay')).toBeHidden();
      await expect.poll(() => page.evaluate(() => monaco.editor.getModels()[0].getValue().replace(/\r\n/g, '\n'))).toBe(code);
      peers[name] = page;
    }
    await expect(owner.locator('#participantsList li')).toHaveCount(4);
    await owner.evaluate(() => {
      const editor = monaco.editor.getEditors()[0];
      editor.setPosition({ lineNumber: 18, column: 7 });
      editor.setScrollTop(0);
    });

    // Produce genuine presence events; no DOM or CSS state is fabricated.
    async function showActivity() {
      for (const page of [peers.Maya, peers.Sam])
        await page.evaluate(() => monaco.editor.getEditors()[0].setPosition({ lineNumber: 1, column: 1 }));
      await peers.Alex.evaluate(() => {
        const editor = monaco.editor.getEditors()[0];
        const model = editor.getModel();
        editor.setPosition({ lineNumber: model.getLineCount(), column: model.getLineMaxColumn(model.getLineCount()) });
        editor.focus();
      });
      await peers.Alex.keyboard.insertText('// Ready to pair.\n');
      await expect(owner.locator('#participantsList li[title="Alex"]')).toHaveClass(/active/);
      await peers.Maya.evaluate(() => monaco.editor.getEditors()[0].setSelection({ startLineNumber: 4, startColumn: 1, endLineNumber: 7, endColumn: 14 }));
      await peers.Sam.evaluate(() => {
        const editor = monaco.editor.getEditors()[0];
        editor.setSelection({ startLineNumber: 10, startColumn: 5, endLineNumber: 13, endColumn: 8 });
        editor.focus();
      });
      await peers.Sam.keyboard.press('Control+c');
      await expect(owner.locator('#participantsList li[title="Maya"]')).toHaveClass(/copied/);
      await expect(owner.locator('#participantsList li[title="Sam"]')).toHaveClass(/copying/);
    }
    await showActivity();
    await screenshot(owner, join(root, 'Room.png'));
    await screenshot(owner, join(images, 'room-dark.png'));
    const bar = await owner.locator('.participants-bar').boundingBox();
    const lastParticipant = await owner.locator('#participantsList li').last().boundingBox();
    await owner.screenshot({ path: join(root, 'UIColors.png'), clip: {
      x: bar.x, y: bar.y, width: lastParticipant.x + lastParticipant.width - bar.x + 20, height: bar.height
    } });
    await setTheme(owner, 'light');
    await showActivity();
    await screenshot(owner, join(images, 'room-light.png'));
    await owner.setViewportSize(mobile);
    await screenshot(owner, join(images, 'room-mobile.png'));
    await owner.setViewportSize(desktop);

  }

  // Create and populate the whiteboard through its real admin and drawing UI.
  await admin.locator('#whiteboardName').fill('System design — Order pipeline');
  await admin.locator('#createWhiteboard').click();
  const boardRow = admin.locator('.room-row').filter({ hasText: 'System design — Order pipeline' });
  await expect(boardRow.locator('.badge')).toHaveText('Whiteboard');
  const boardPath = await boardRow.locator('a').getAttribute('href');
  const board = await newPage();
  await board.context().addCookies(await admin.context().cookies());
  const boardPeer = await newPage();
  const cursorPeer = cursorsOnly ? await newPage() : null;
  for (const [page, name] of [[board, 'Nikita'], [boardPeer, 'Maya']]) {
    await page.goto(boardPath);
    await page.locator('#displayNameInput').fill(name);
    await page.locator('#joinBtn').click();
    await expect(page.locator('#joinOverlay')).toBeHidden();
  }
  await expect(board.locator('#participantsList li')).toHaveCount(2);
  async function draw(type, x, y, width, height, label) {
    await board.bringToFront();
    await board.locator(`button[data-tool="${type}"]`).click();
    const canvas = await board.locator('.board-canvas').boundingBox();
    await board.mouse.move(canvas.x + x, canvas.y + y);
    await board.mouse.down();
    await board.mouse.move(canvas.x + x + width, canvas.y + y + height, { steps: 3 });
    await board.mouse.up();
    if (label) {
      await board.getByRole('button', { name: 'Edit label', exact: true }).click();
      const input = board.getByRole('textbox', { name: 'Element label' });
      await input.fill(label);
      await input.press('Enter');
    }
  }
  await draw('rectangle', 100, 150, 150, 90, 'Client');
  await draw('rectangle', 380, 150, 190, 90, 'Order API');
  await draw('database', 720, 135, 170, 120, 'Orders DB');
  await draw('queue', 365, 350, 220, 80, 'Events');
  await draw('rectangle', 720, 345, 170, 90, 'Workers');
  await draw('bidirectional-arrow', 260, 195, 110, 0);
  await draw('bidirectional-arrow', 580, 195, 130, 0);
  await draw('arrow', 475, 250, 0, 90);
  await draw('arrow', 595, 390, 115, 0);
  await expect(boardPeer.locator('[data-shape-id]')).toHaveCount(9);
  await expect(boardPeer.locator('[data-shape-type="database"]')).toHaveText('Orders DB');
  for (const page of [board, boardPeer]) {
    const arrows = page.locator('[data-shape-type="bidirectional-arrow"]');
    await expect(arrows).toHaveCount(2);
    await expect(arrows.locator('path')).toHaveCount(4);
  }
  if (cursorPeer) {
    await cursorPeer.goto(boardPath);
    await cursorPeer.locator('#displayNameInput').fill('Leo');
    await cursorPeer.locator('#joinBtn').click();
    await expect(cursorPeer.locator('#joinOverlay')).toBeHidden();
    await expect(board.locator('#participantsList li')).toHaveCount(3);
  }
  await board.locator('.board-canvas').press('Escape');
  await board.getByRole('button', { name: 'Fit diagram' }).click();
  await setTheme(board, 'dark');
  async function showBoardCursors() {
    // Drive real participant pages; the viewer receives positions through SignalR.
    for (const [peer, x, y] of [[boardPeer, 560, 230], [cursorPeer, 700, 360]]) {
      const canvas = await peer.locator('.board-canvas').boundingBox();
      await peer.mouse.move(canvas.x + x, canvas.y + y);
    }
    await expect(board.locator('[data-cursor-name]')).toHaveCount(2);
    for (const name of ['Maya', 'Leo']) await expect(board.locator(`[data-cursor-name="${name}"]`)).toBeVisible();
  }
  if (cursorsOnly) await showBoardCursors();
  await screenshot(board, join(images, cursorsOnly ? 'whiteboard-cursors-dark.png' : 'whiteboard-dark.png'));
  await setTheme(board, 'light');
  if (cursorsOnly) await showBoardCursors();
  await screenshot(board, join(images, cursorsOnly ? 'whiteboard-cursors-light.png' : 'whiteboard-light.png'));
  if (!cursorsOnly) {
    await board.setViewportSize(mobile);
    await board.getByRole('button', { name: 'Fit diagram' }).click();
    await screenshot(board, join(images, 'whiteboard-mobile.png'));
  }

  if (!whiteboardOnly) {
    await admin.reload();
    await expect(admin.locator('#rooms .room-row').filter({ hasText: 'Friday pairing' })).toContainText('4 users');
    await expect(admin.locator('#rooms .room-row').filter({ hasText: 'System design — Order pipeline' })).toContainText('2 users');
    await screenshot(admin, join(images, 'admin-rooms.png'));
    await setTheme(admin, 'light');
    await screenshot(admin, join(images, 'admin-rooms-light.png'));
    await admin.setViewportSize(mobile);
    await screenshot(admin, join(images, 'admin-mobile.png'));
    await admin.setViewportSize(desktop);
    await setTheme(admin, 'dark');
    await admin.setViewportSize({ width: 1440, height: 1200 });
    await admin.getByRole('link', { name: 'Open question library' }).click();
    const library = admin.frameLocator('iframe[title="Question library"]');
    await library.getByRole('button', { name: 'C#', exact: true }).click();
    await expect(library.locator('.snippet-code [class^="mtk"]')).not.toHaveCount(0);
    await expect(library.locator('#snippetSyncTime')).toHaveAttribute('datetime', /\d{4}-\d{2}-\d{2}T/);
    await expect(library.locator('.snippet-step')).toHaveCount(2);
    await expect(library.locator('.snippet-step').nth(1).locator('.snippet-code [class^="mtk"]')).not.toHaveCount(0);
    await screenshot(admin, join(images, 'snippets-popup.png'));
    const [libraryTab] = await Promise.all([
      admin.context().waitForEvent('page'),
      admin.getByRole('link', { name: 'Open in new tab' }).click()
    ]);
    await libraryTab.setViewportSize({ width: 1440, height: 1000 });
    await libraryTab.getByRole('button', { name: 'SQL', exact: true }).click();
    await expect(libraryTab.locator('.snippet-code [class^="mtk"]')).not.toHaveCount(0);
    await expect(libraryTab.locator('#snippetSyncTime')).toHaveAttribute('datetime', /\d{4}-\d{2}-\d{2}T/);
    await expect(libraryTab.locator('.snippet-step')).toHaveCount(2);
    await expect(libraryTab.locator('.snippet-step').nth(1).locator('.snippet-code [class^="mtk"]')).not.toHaveCount(0);
    await screenshot(libraryTab, join(images, 'snippets-tab.png'));
    await setTheme(admin, 'light');
    await expect(libraryTab.locator('html')).toHaveAttribute('data-theme', 'light');
    await screenshot(libraryTab, join(images, 'snippets-tab-light.png'));
    cpSync(join(images, 'snippets-tab-light.png'), join(root, 'src/SealCode/wwwroot/product/snippets-tab-light.png'));
    await libraryTab.close();
    await admin.getByRole('button', { name: 'Close' }).click();
  }
  expect(errors, 'Screenshot scenarios must not hide browser runtime errors').toEqual([]);
  console.log(cursorsOnly ? 'README cursor screenshots refreshed from three live participants.' : whiteboardOnly ? 'README whiteboard screenshots refreshed with bidirectional arrows.' : 'README screenshots refreshed with three code rooms, one whiteboard and six participants.');
} finally {
  await browser?.close();
  if (server && server.exitCode === null) {
    const exited = once(server, 'exit');
    server.kill();
    await exited;
  }
  const cleanupPath = resolve(temporaryRoot);
  if (dirname(cleanupPath) !== resolve(tmpdir()) || !basename(cleanupPath).startsWith('sealcode-readme-'))
    throw new Error('Refusing to remove a directory outside the screenshot temporary root');
  rmSync(cleanupPath, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 });
}
