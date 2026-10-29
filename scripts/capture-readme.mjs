import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { mkdirSync, mkdtempSync, writeFileSync, rmSync } from 'node:fs';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import { basename, dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium, expect } from '@playwright/test';

// Capture the real application using disposable local accounts and room data.
// Never reads or changes the developer's appsettings.json or running server.
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const images = join(root, 'docs', 'images');
const temporaryRoot = mkdtempSync(join(tmpdir(), 'sealcode-readme-'));
const desktop = { width: 1440, height: 900 };
const mobile = { width: 390, height: 844 };
let server;
let browser;

async function run(command, args) {
  const child = spawn(command, args, { cwd: root, stdio: 'inherit', windowsHide: true });
  const [code] = await once(child, 'exit');
  if (code !== 0) throw new Error(`${command} exited with ${code}`);
}

async function screenshot(page, path) {
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
  await run(process.execPath, ['scripts/build-client.mjs']);
  await run('dotnet', ['build', 'src/SealCode/SealCode.csproj', '-c', 'Debug', '-p:SkipClientBuild=true']);

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
    Logging: { LogLevel: { Default: 'Warning' } }
  }));
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
  await screenshot(admin, join(images, 'admin-login.png'));
  await admin.locator('#name').fill('DemoAdmin');
  await admin.locator('#password').fill('local-screenshot-demo');
  await admin.locator('button[type="submit"]').click();
  await expect(admin).toHaveURL(`${baseURL}/admin`);

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
  const bar = await owner.locator('.participants-bar').boundingBox();
  const lastParticipant = await owner.locator('#participantsList li').last().boundingBox();
  await owner.screenshot({ path: join(root, 'UIColors.png'), clip: {
    x: bar.x, y: bar.y, width: lastParticipant.x + lastParticipant.width - bar.x + 20, height: bar.height
  } });
  await setTheme(owner, 'light');
  await showActivity();
  await screenshot(owner, join(images, 'room-light.png'));

  await admin.reload();
  await expect(admin.locator('#rooms .room-row').filter({ hasText: 'Friday pairing' })).toContainText('4 users');
  await screenshot(admin, join(images, 'admin-rooms.png'));
  await setTheme(admin, 'light');
  await admin.setViewportSize(mobile);
  await screenshot(admin, join(images, 'admin-mobile.png'));
  // This command captures documentation, while the browser suite gates behavior.
  // Keep runtime diagnostics visible even when all requested screenshots were captured.
  for (const message of errors) console.warn(`Browser diagnostic during capture: ${message}`);
  console.log('README screenshots refreshed using four connected participants.');
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
