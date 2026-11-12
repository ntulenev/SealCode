import { test, expect } from '@playwright/test';

// Apply the network contract to every demo scenario, including reloads and edits.
// Starting at the demo document lets the navigation test visit the real homepage first.
const failures = new WeakMap();
test.beforeEach(async ({ page, context, baseURL }) => {
  const problems = [];
  failures.set(page, problems);
  function watchPage(page) {
    let inDemo = false;
    page.on('request', request => {
      const url = new URL(request.url());
      if (url.pathname === '/demo.html') inDemo = true;
      if (!inDemo) return;
      const asset = /^\/(demo\.html|theme\.js|site\.css|whiteboard\.css|demo\.css|Logo\.png|favicon\.ico|assets\/[\w.-]+\.(js|css|ttf))$/;
      if (url.origin !== new URL(baseURL).origin || request.method() !== 'GET' ||
          ['fetch', 'xhr', 'eventsource'].includes(request.resourceType()) || !asset.test(url.pathname)) {
        problems.push(`Unexpected request: ${request.method()} ${request.url()}`);
      }
    });
    page.on('websocket', socket => problems.push(`Unexpected WebSocket: ${socket.url()}`));
    page.on('response', response => {
      if (inDemo && response.status() >= 400) problems.push(`HTTP ${response.status()}: ${response.url()}`);
    });
    page.on('pageerror', error => problems.push(error.message));
    page.on('console', message => {
      if (inDemo && message.type() === 'error') problems.push(message.text());
    });
  }
  watchPage(page);
  context.on('page', watchPage);
});

test.afterEach(async ({ page }) => {
  expect(failures.get(page), 'Demo must load successfully without API calls or runtime errors').toEqual([]);
});

async function editCode(page, text) {
  await page.locator('#demoEditor .view-lines').click();
  await page.keyboard.press('Control+Home');
  await page.keyboard.type(text);
  await expect(page.locator('#demoEditor .view-lines')).toContainText(text);
}

async function editService(page, text) {
  await page.locator('[data-shape-id="api"]').click();
  await page.getByRole('button', { name: 'Edit label', exact: true }).click();
  const label = page.getByRole('textbox', { name: 'Element label' });
  await label.fill(text);
  await label.press('Enter');
  await expect(page.locator('[data-shape-id="api"]')).toHaveText(text);
}

test('public demo is editable, resettable and uses static assets only @smoke', async ({ page }) => {
  await page.goto('/demo.html');
  await expect(page.getByRole('heading', { name: 'Try a few lines of code' })).toBeVisible();
  await expect(page.locator('#demoEditor .monaco-editor')).toBeVisible();
  await expect(page.locator('[data-shape-id]')).toHaveCount(7);
  await expect(page.locator('[data-shape-type="rectangle"]')).toHaveCount(2);
  await expect(page.locator('[data-shape-id="queue"]')).toHaveAttribute('data-shape-type', 'queue');
  await expect(page.locator('[data-shape-id="db"]')).toHaveAttribute('data-shape-type', 'database');
  const remote = page.locator('.board-remote-cursor');
  const initialCursor = await remote.getAttribute('style');
  await expect.poll(() => remote.getAttribute('style')).not.toBe(initialCursor);
  await page.getByRole('button', { name: 'Pause demo cursors' }).click();
  await expect(page.getByRole('button', { name: 'Resume demo cursors' })).toHaveAttribute('aria-pressed', 'true');

  await editCode(page, '// visitor edit');
  await page.keyboard.press('Control+z');
  await expect(page.locator('#demoEditor .view-lines')).not.toContainText('visitor edit');
  await page.keyboard.press('Control+Shift+z');
  await expect(page.locator('#demoEditor .view-lines')).toContainText('visitor edit');
  await page.getByRole('button', { name: 'Reset code', exact: true }).click();
  await expect(page.locator('#demoEditor .view-lines')).not.toContainText('visitor edit');

  await editService(page, 'Visitor service');
  await page.getByRole('button', { name: 'Undo', exact: true }).click();
  await expect(page.locator('[data-shape-id="api"]')).toHaveText('Orders API');
  await page.getByRole('button', { name: 'Redo', exact: true }).click();
  await expect(page.locator('[data-shape-id="api"]')).toHaveText('Visitor service');
  await page.getByRole('button', { name: 'Reset diagram', exact: true }).click();
  await expect(page.locator('[data-shape-id="api"]')).toHaveText('Orders API');
  await expect(page.locator('[data-shape-id]')).toHaveCount(7);
  await page.screenshot({ path: 'test-results/demo-desktop.png', fullPage: true });
});

test('demo fits mobile, supports light theme and keeps reduced-motion cursors still', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.goto('/demo.html');
  const remote = page.locator('.board-remote-cursor');
  await expect(remote).toHaveCount(1);
  const initial = await remote.getAttribute('style');
  const codeCursor = page.locator('.demo-code-cursor');
  const initialCode = await codeCursor.getAttribute('style');
  // Wait across two actual demo ticks. Fake performance APIs break Monaco's
  // input-latency instrumentation, so these integration tests use the real clock.
  await page.waitForTimeout(3800);
  await expect(remote).toHaveAttribute('style', initial);
  await expect(codeCursor).toHaveAttribute('style', initialCode);
  await page.locator('[data-theme-toggle]').click();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.screenshot({ path: 'test-results/demo-mobile-light.png', fullPage: true });
});

test('both simulated cursors move, pause and resume without changing visitor content', async ({ page }) => {
  await page.goto('/demo.html');
  const boardCursor = page.locator('.board-remote-cursor');
  const codeCursor = page.locator('.demo-code-cursor');
  await expect(codeCursor).toBeAttached();
  await expect(boardCursor).toHaveCount(1);
  // Change the example to a single short line: mock positions must remain valid.
  await page.locator('#demoEditor .view-lines').click();
  await page.keyboard.press('Control+a');
  const content = 'const local = 1;';
  await page.keyboard.type(content);
  await expect(page.locator('#demoEditor .view-lines')).toHaveText(content);
  const diagram = await page.locator('[data-shape-id]').allTextContents();
  const beforeBoard = await boardCursor.getAttribute('style');
  await expect.poll(() => boardCursor.getAttribute('style')).not.toBe(beforeBoard);
  await expect(page.locator('#demoEditor .view-lines')).toHaveText(content);
  // Restore the longer example so editor movement is visually observable too.
  await page.getByRole('button', { name: 'Reset code', exact: true }).click();
  const beforeCode = await codeCursor.getAttribute('style');
  await expect.poll(() => codeCursor.getAttribute('style')).not.toBe(beforeCode);
  const restoredContent = await page.locator('#demoEditor .view-lines').innerText();
  await page.getByRole('button', { name: 'Pause demo cursors' }).click();
  const pausedCode = await codeCursor.getAttribute('style');
  const pausedBoard = await boardCursor.getAttribute('style');
  await page.waitForTimeout(3800);
  await expect(codeCursor).toHaveAttribute('style', pausedCode);
  await expect(boardCursor).toHaveAttribute('style', pausedBoard);
  await page.getByRole('button', { name: 'Resume demo cursors' }).click();
  await expect.poll(() => codeCursor.getAttribute('style')).not.toBe(pausedCode);
  await expect.poll(() => boardCursor.getAttribute('style')).not.toBe(pausedBoard);
  await expect(page.locator('#demoEditor .view-lines')).toHaveText(restoredContent, { useInnerText: true });
  expect(await page.locator('[data-shape-id]').allTextContents()).toEqual(diagram);
});

test('resets are independent and reloading discards visitor edits', async ({ page }) => {
  await page.goto('/demo.html');
  await expect(page.locator('[data-shape-id]')).toHaveCount(7);
  await editCode(page, '// private draft');
  await editService(page, 'Private service');
  await page.getByRole('button', { name: 'Reset code', exact: true }).click();
  await expect(page.locator('#demoEditor .view-lines')).not.toContainText('private draft');
  await expect(page.locator('[data-shape-id="api"]')).toHaveText('Private service');
  await editCode(page, '// private draft');
  await page.getByRole('button', { name: 'Reset diagram', exact: true }).click();
  await expect(page.locator('[data-shape-id="api"]')).toHaveText('Orders API');
  await expect(page.locator('#demoEditor .view-lines')).toContainText('private draft');
  // Reset must also clear the whiteboard's undo history.
  await page.getByRole('button', { name: 'Undo', exact: true }).click();
  await expect(page.locator('[data-shape-id="api"]')).toHaveText('Orders API');
  await expect(page.locator('[data-shape-id]')).toHaveCount(7);
  await editService(page, 'Private service');
  await page.reload();
  await expect(page.locator('#demoEditor .view-lines')).toContainText('readyToShip');
  await expect(page.locator('#demoEditor .view-lines')).not.toContainText('private draft');
  await expect(page.locator('[data-shape-id="api"]')).toHaveText('Orders API');
});

test('visitor can draw, move, zoom and undo diagram changes', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 1000 });
  await page.goto('/demo.html');
  const canvas = page.locator('.board-canvas');
  await canvas.scrollIntoViewIfNeeded();
  await canvas.focus();
  await page.getByRole('button', { name: 'Rectangle (R)', exact: true }).click();
  const box = await canvas.boundingBox();
  await page.mouse.move(box.x + 450, box.y + 340);
  await page.mouse.down();
  await page.mouse.move(box.x + 610, box.y + 420, { steps: 5 });
  await page.mouse.up();
  await expect(page.locator('[data-shape-id]')).toHaveCount(8);
  const added = page.locator('[data-shape-type="rectangle"]').last();
  const id = await added.getAttribute('data-shape-id');
  const rect = page.locator(`[data-shape-id="${id}"] rect`);
  const originalX = await rect.getAttribute('x');
  const shapeBox = await added.boundingBox();
  await page.mouse.move(shapeBox.x + 30, shapeBox.y + 30);
  await page.mouse.down();
  await page.mouse.move(shapeBox.x + 80, shapeBox.y + 55, { steps: 5 });
  await page.mouse.up();
  await expect(rect).not.toHaveAttribute('x', originalX);
  await page.getByRole('button', { name: 'Undo', exact: true }).click();
  await expect(rect).toHaveAttribute('x', originalX);
  await page.getByRole('button', { name: 'Undo', exact: true }).click();
  await expect(page.locator('[data-shape-id]')).toHaveCount(7);
  await page.getByRole('button', { name: 'Redo', exact: true }).click();
  await expect(page.locator('[data-shape-id]')).toHaveCount(8);
  const resetView = page.getByRole('button', { name: 'Reset view', exact: true });
  const zoom = await resetView.textContent();
  await page.getByRole('button', { name: 'Zoom in', exact: true }).click();
  await expect(resetView).not.toHaveText(zoom);
  await expect(page.locator('[data-shape-id]')).toHaveCount(8);
  await page.getByRole('button', { name: 'Pan (H)', exact: true }).click();
  const layer = canvas.locator(':scope > g').first();
  const transform = await layer.getAttribute('transform');
  await page.mouse.move(box.x + 700, box.y + 370);
  await page.mouse.down();
  await page.mouse.move(box.x + 740, box.y + 410, { steps: 5 });
  await page.mouse.up();
  await expect(layer).not.toHaveAttribute('transform', transform);
  await expect(page.locator('[data-shape-id]')).toHaveCount(8);
});

test('two tabs keep separate practice documents', async ({ page, context }) => {
  await page.goto('/demo.html');
  await expect(page.locator('[data-shape-id]')).toHaveCount(7);
  await editCode(page, '// first tab');
  await editService(page, 'First tab service');
  const other = await context.newPage();
  try {
    await other.goto('/demo.html');
    await expect(other.locator('#demoEditor .view-lines')).toContainText('readyToShip');
    await expect(other.locator('#demoEditor .view-lines')).not.toContainText('first tab');
    await expect(other.locator('[data-shape-id="api"]')).toHaveText('Orders API');
    await editCode(other, '// second tab');
    await editService(other, 'Second tab service');
    await expect(page.locator('#demoEditor .view-lines')).toContainText('first tab');
    await expect(page.locator('#demoEditor .view-lines')).not.toContainText('second tab');
    await expect(page.locator('[data-shape-id="api"]')).toHaveText('First tab service');
  } finally {
    await other.close();
  }
});

test('homepage links to demo and both practice links navigate to their workspaces', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('link', { name: 'Try demo', exact: true }).click();
  await expect(page).toHaveURL(/\/demo\.html$/);
  await expect(page.locator('#demoEditor .monaco-editor')).toBeVisible();
  await page.getByRole('link', { name: 'Try the whiteboard' }).click();
  await expect(page).toHaveURL(/#board$/);
  await expect(page.locator('.board-canvas')).toBeInViewport();
  await page.getByRole('link', { name: 'Try the editor' }).click();
  await expect(page).toHaveURL(/#code$/);
  await expect(page.locator('#demoEditor')).toBeInViewport();
});
