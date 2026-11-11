import { test, expect } from './fixtures.js';
import { csrf } from './admin-token.js';

const connection = process.env.SEALCODE_SNIPPETS_EDITOR_TEST_CONNECTION;

test('super admin previews and publishes snippets with immediate cache refresh and conflict protection', async ({ browser, isolatedHost }, testInfo) => {
  test.skip(!connection, 'Requires a disposable migrated database with a seeded general/1 question.');
  const host = await isolatedHost({
    Snippets: { Enabled: true, Provider: 'PostgreSql' },
    ConnectionStrings: { SealCode: connection },
    AdminUsers: [
      { Name: 'BrowserAdmin', Password: 'browser-pass', IsSuperAdmin: true },
      { Name: 'RegularAdmin', Password: 'regular-pass', IsSuperAdmin: false }
    ]
  });
  const context = await browser.newContext({ baseURL: host.baseURL, viewport: { width: 1440, height: 1000 } });
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  try {
    const anonymous = await context.request.get('/admin/snippets/manage/catalog');
    expect(anonymous.status()).toBe(401);
    await context.request.post('/admin/login', { headers: await csrf(context.request), form: { name: 'BrowserAdmin', password: 'browser-pass' } });
    const initial = await (await context.request.get('/admin/snippets/manage/catalog')).json();
    const reset = await context.request.post('/admin/snippets/manage/general/1', {
      headers: await csrf(context.request), data: { ExpectedVersion: initial.Version, Topic: 'Original', Description: 'Coverage', Steps: [{ Title: 'Original example', Markdown: 'Original source' }] }
    });
    expect(reset.status()).toBe(200);
    await page.goto('/admin');
    await page.getByRole('link', { name: 'Edit snippets', exact: true }).click();
    await expect(page.locator('#snippetEditForm')).toBeVisible();
    await page.locator('#editCategory').selectOption('general');
    await page.locator('#editQuestion').selectOption('1');
    const before = await (await context.request.get('/admin/snippets/manage/catalog')).json();
    if (process.env.CAPTURE_SNIPPET_EDITOR) {
      await page.locator('#categoryManagement summary').click();
      await page.locator('#editTopic').fill('Cancellation in asynchronous requests');
      await page.locator('#editDescription').fill('Compare request cancellation, token propagation and predictable cleanup.');
      await page.locator('#editSteps input').first().fill('Pass cancellation to the HTTP request');
      await page.locator('#editSteps textarea').first().fill('What happens when the caller cancels this request?\n\n```csharp\nusing var cancellation = new CancellationTokenSource();\ncancellation.CancelAfter(TimeSpan.FromSeconds(5));\n\nvar response = await client.GetAsync(url, cancellation.Token);\nresponse.EnsureSuccessStatusCode();\n```');
      await page.screenshot({ path: 'docs/images/snippet-editor.png', fullPage: true });
      await page.getByRole('button', { name: 'Preview before publishing' }).click();
      await expect(page.getByRole('button', { name: 'Publish changes', exact: true })).toBeEnabled();
      await page.getByRole('button', { name: 'Switch to light theme' }).click();
      await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
      await page.screenshot({ path: 'docs/images/snippet-editor-preview.png', fullPage: true });
      await page.getByRole('button', { name: 'Switch to dark theme' }).click();
    }
    await page.locator('#editTopic').fill('Published from browser');
    await page.locator('#editSteps textarea').first().fill('Preview <script>alert(1)</script>\n\n```sql\nSELECT 42;\n```');
    await page.getByRole('button', { name: 'Add example +' }).click();
    await page.locator('#editSteps input').last().fill('Follow-up');
    await page.locator('#editSteps textarea').last().fill('```sql\nSELECT 43;\n```');
    await expect(page.getByRole('button', { name: 'Publish changes', exact: true })).toBeDisabled();
    await page.getByRole('button', { name: 'Preview before publishing' }).click();
    await expect(page.locator('#editPreview .snippet-code')).toHaveCount(2);
    await expect(page.locator('#editPreview script')).toHaveCount(0);
    await expect(page.locator('#editPreview')).toContainText('<script>alert(1)</script>');
    await expect(page.getByRole('button', { name: 'Publish changes', exact: true })).toBeEnabled();
    await page.screenshot({ path: testInfo.outputPath('snippet-editor-preview.png'), fullPage: true });
    for (const width of [375, 1440]) {
      await page.setViewportSize({ width, height: 1000 });
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    }
    await page.getByRole('button', { name: 'Publish changes', exact: true }).click();
    await expect(page.locator('#editStatus')).toHaveText('Published. The application cache is updated.');
    // This request immediately reads the public in-memory catalog; no refresh timer is advanced.
    const cached = await (await context.request.get('/admin/snippets/catalog')).json();
    const updated = cached.find(item => item.Language === 'general').Questions.find(item => item.Number === 1);
    expect(updated.Topic).toBe('Published from browser');
    expect(updated.Steps).toHaveLength(2);
    expect(updated.Steps[1].Markdown).toContain('SELECT 43;');
    const stale = await context.request.post('/admin/snippets/manage/general/1', {
      headers: await csrf(context.request), data: { ExpectedVersion: before.Version, Topic: 'stale', Description: 'stale', Steps: [{ Title: 'stale', Markdown: 'stale' }] }
    });
    expect(stale.status()).toBe(409);
    const unsafe = await context.request.post('/admin/snippets/manage/general/1', { data: {} });
    expect(unsafe.status()).toBe(400);
    await page.locator('#editTopic').fill('Unsaved local draft');
    const fresh = await (await context.request.get('/admin/snippets/manage/catalog')).json();
    const remote = await context.request.post('/admin/snippets/manage/general/1', {
      headers: await csrf(context.request), data: { ExpectedVersion: fresh.Version, Topic: 'Remote edit', Description: 'Coverage', Steps: [{ Title: 'Remote', Markdown: 'Remote source' }] }
    });
    expect(remote.status()).toBe(200);
    await page.getByRole('button', { name: 'Preview before publishing' }).click();
    await page.getByRole('button', { name: 'Publish changes', exact: true }).click();
    await expect(page.locator('#editStatus')).toContainText('Another editor changed the library');
    await expect(page.locator('#editTopic')).toHaveValue('Unsaved local draft');
    expect(errors).toEqual([]);
    // Drop the local draft explicitly before navigating in teardown.
    page.on('dialog', dialog => dialog.accept());
    await context.request.post('/admin/logout', { headers: await csrf(context.request) });
    await context.request.post('/admin/login', { headers: await csrf(context.request), form: { name: 'RegularAdmin', password: 'regular-pass' } });
    expect((await context.request.get('/admin/snippets/manage')).status()).toBe(403);
    expect((await context.request.get('/admin/snippets/manage/catalog')).status()).toBe(403);
    expect((await context.request.post('/admin/snippets/manage/general/1', { headers: await csrf(context.request), data: {} })).status()).toBe(403);
    await page.goto('/admin');
    await expect(page.getByRole('link', { name: 'Edit snippets', exact: true })).toHaveCount(0);
  } finally { await context.close(); }
});


test('super admin manages category and question lifecycles with immediately visible cache changes', async ({ browser, isolatedHost }) => {
  test.skip(!connection, 'Requires a disposable migrated snippet database.');
  const host = await isolatedHost({ Snippets: { Enabled: true, Provider: 'PostgreSql' }, ConnectionStrings: { SealCode: connection } });
  const context = await browser.newContext({ baseURL: host.baseURL, viewport: { width: 1440, height: 1000 } });
  const page = await context.newPage();
  page.on('dialog', dialog => dialog.accept());
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  const cached = async () => (await context.request.get('/admin/snippets/catalog')).json();
  try {
    await context.request.post('/admin/login', { headers: await csrf(context.request), form: { name: 'BrowserAdmin', password: 'browser-pass' } });
    await page.goto('/admin/snippets/manage');
    await page.locator('#categoryManagement summary').click();
    await page.getByRole('button', { name: 'New category', exact: true }).click();
    await page.locator('#categoryKey').fill('sql');
    await page.locator('#categoryTitle').fill('SQL interview questions');
    await page.locator('#categorySortOrder').fill('-10');
    await page.getByRole('button', { name: 'Save category', exact: true }).click();
    await expect(page.locator('#editStatus')).toContainText('Category saved');
    await expect(page.locator('#snippetEditForm')).toBeHidden();
    expect((await cached()).find(item => item.Language === 'sql').Questions).toHaveLength(0);
    await page.getByRole('button', { name: 'New question +', exact: true }).click();
    await page.locator('#editTopic').fill('Transactions');
    await page.locator('#editDescription').fill('Isolation and atomicity');
    await page.locator('#editSteps input').fill('Original');
    await page.locator('#editSteps textarea').fill('```sql\nBEGIN;\nCOMMIT;\n```');
    await page.getByRole('button', { name: 'Preview before publishing' }).click();
    await page.getByRole('button', { name: 'Publish changes', exact: true }).click();
    await expect(page.locator('#editStatus')).toContainText('Published.');
    expect((await cached()).find(item => item.Language === 'sql').Questions[0].Topic).toBe('Transactions');
    await page.locator('#categoryKey').fill('javascript');
    await page.locator('#categoryTitle').fill('JavaScript interview questions');
    await page.getByRole('button', { name: 'Save category', exact: true }).click();
    await expect(page.locator('#editCategory')).toHaveValue('javascript');
    const renamed = await cached();
    expect(renamed.find(item => item.Language === 'sql')).toBeUndefined();
    expect(renamed[0].Title).toBe('JavaScript interview questions');
    expect(renamed[0].Questions[0].Steps[0].Markdown).toContain('COMMIT');
    await page.getByRole('button', { name: 'New question +', exact: true }).click();
    await page.locator('#editTopic').fill('Promises');
    await page.locator('#editDescription').fill('Promise ordering');
    await page.locator('#editSteps input').fill('Example');
    await page.locator('#editSteps textarea').fill('Promise.resolve(42);');
    await page.getByRole('button', { name: 'Preview before publishing' }).click();
    await page.getByRole('button', { name: 'Publish changes', exact: true }).click();
    await expect(page.locator('#editQuestion')).toHaveValue('2');
    await page.locator('#editQuestion').selectOption('1');
    await page.locator('#editQuestionNumber').fill('2');
    await page.getByRole('button', { name: 'Preview before publishing' }).click();
    await page.getByRole('button', { name: 'Publish changes', exact: true }).click();
    await expect(page.locator('#editStatus')).toContainText('destination is occupied');
    expect((await cached()).find(item => item.Language === 'javascript').Questions).toHaveLength(2);
    await page.getByRole('button', { name: 'Reload library' }).click();
    await expect(page.locator('#editQuestionNumber')).toHaveValue('1');
    await page.locator('#editQuestionCategory').selectOption('general');
    await page.locator('#editQuestionNumber').fill('42');
    await page.getByRole('button', { name: 'Preview before publishing' }).click();
    await page.getByRole('button', { name: 'Publish changes', exact: true }).click();
    await expect(page.locator('#editCategory')).toHaveValue('general');
    await expect(page.locator('#editQuestion')).toHaveValue('42');
    expect((await cached()).find(item => item.Language === 'general').Questions.find(item => item.Number === 42).Topic).toBe('Transactions');
    await page.getByRole('button', { name: 'Delete question', exact: true }).click();
    await expect(page.locator('#editStatus')).toContainText('Question deleted');
    expect((await cached()).find(item => item.Language === 'general').Questions.some(item => item.Number === 42)).toBe(false);
    await page.locator('#editCategory').selectOption('javascript');
    await page.getByRole('button', { name: 'Delete category', exact: true }).click();
    await expect(page.locator('#editStatus')).toContainText('Category and its questions deleted');
    expect((await cached()).some(item => item.Language === 'javascript')).toBe(false);
    expect(errors).toEqual([]);
  } finally { await context.close(); }
});
