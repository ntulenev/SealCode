import { expect, test } from './fixtures.js';
import { csrf } from './admin-token.js';

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
  expect(workers.some((url) => url.endsWith('/assets/ts.worker.js'))).toBeTruthy();
  expect(external).toEqual([]);
  expect(errors).toEqual([]);
  expect(await page.evaluate(() => window.cspViolations)).toEqual([]);
});
