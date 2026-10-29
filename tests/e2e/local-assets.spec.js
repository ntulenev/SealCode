import { expect, test } from './fixtures.js';
import { csrf } from './admin-token.js';

test('every page versions its assets, including images, icons and admin imports', async ({ page, request }) => {
  const assetRequests = [];
  const failures = [];
  page.on('request', (request) => {
    if (/\.(?:js|css|png|ico|ttf)$/.test(new URL(request.url()).pathname))
      assetRequests.push(request.url());
  });
  page.on('pageerror', (error) => failures.push(error.message));
  page.on('response', (response) => {
    if (response.status() >= 400) failures.push(`${response.status()} ${response.url()}`);
  });
  const created = await request.post('/admin/rooms', {
    headers: await csrf(request), data: { Name: 'Versioned assets', Language: 'javascript' }
  });
  let version;
  for (const path of ['/', '/about.html', '/admin/login', '/admin', `/room/${(await created.json()).RoomId}`]) {
    const response = await page.goto(path);
    expect(response.headers()['cache-control']).toContain('no-cache');
    const urls = await page.locator('script[src], link[rel="stylesheet"], link[rel="icon"], img[src]')
      .evaluateAll((elements) => elements.map((element) => element.getAttribute('src') || element.getAttribute('href')));
    expect(urls.length).toBeGreaterThan(0);
    for (const url of urls) {
      const assetVersion = new URL(url, page.url()).searchParams.get('v');
      expect(assetVersion, url).toMatch(/^\d+\.\d+\.\d+$/);
      version ??= assetVersion;
      expect(assetVersion, url).toBe(version);
    }
    expect(await page.locator('img').evaluateAll((images) => images.every((image) => image.complete && image.naturalWidth > 0))).toBe(true);
  }
  for (const url of assetRequests) {
    const asset = new URL(url);
    if (!/-[A-Z0-9]{8}\./.test(asset.pathname))
      expect(asset.searchParams.get('v'), url).toBe(version);
  }
  expect(failures).toEqual([]);
});

test('room runs with local assets and CSP, including a language worker', async ({
  page,
  baseURL
}) => {
  const asset = await page.request.get('/assets/room.js');
  expect(asset.headers()['cache-control']).toContain('no-cache');
  const errors = [];
  const external = [];
  const workers = [];
  page.on('pageerror', (error) => errors.push(error.message));
  page.on('request', (request) => {
    if (
      !request.url().startsWith(baseURL) &&
      !request.url().startsWith(baseURL.replace('http', 'ws'))
    )
      external.push(request.url());
  });
  page.on('worker', (worker) => workers.push(worker.url()));
  await page.addInitScript(() => {
    window.cspViolations = [];
    document.addEventListener('securitypolicyviolation', (event) =>
      window.cspViolations.push(event.violatedDirective)
    );
  });
  const created = await page.request.post('/admin/rooms', {
    headers: await csrf(page.request),
    data: { Name: 'Local assets', Language: 'javascript' }
  });
  const response = await page.goto(`/room/${(await created.json()).RoomId}`);
  expect(response.headers()['content-security-policy']).toContain("script-src 'self'");
  await page.locator('#displayNameInput').fill('Alice');
  await page.locator('#joinBtn').click();
  await expect(page.locator('#joinOverlay')).toHaveClass(/hidden/);
  await page.evaluate(async () => {
    const model = monaco.editor.getModels()[0];
    model.setValue('const answer = 42;');
    const factory = await monaco.languages.typescript.getJavaScriptWorker();
    const worker = await factory(model.uri);
    await worker.getSyntacticDiagnostics(model.uri.toString());
  });
  const entryUrl = await page.locator('script[src*="/assets/room.js"]').getAttribute('src');
  const version = new URL(entryUrl, baseURL).searchParams.get('v');
  expect(workers.some((url) => new URL(url).pathname === '/assets/ts.worker.js')).toBeTruthy();
  for (const url of workers) expect(new URL(url).searchParams.get('v')).toBe(version);
  expect(external).toEqual([]);
  expect(errors).toEqual([]);
  expect(await page.evaluate(() => window.cspViolations)).toEqual([]);
});
