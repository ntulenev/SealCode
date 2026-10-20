import { expect, test } from '@playwright/test';
import { csrf } from '../e2e/admin-token.js';

test('production authenticates hashes and issues a Secure cookie over real HTTPS', async ({
  request
}) => {
  const response = await request.post('/admin/login', {
    headers: await csrf(request),
    form: { name: 'BrowserAdmin', password: 'browser-pass' },
    maxRedirects: 0
  });
  expect(response.status()).toBe(302);
  const cookie = response
    .headersArray()
    .find(
      (header) =>
        header.name.toLowerCase() === 'set-cookie' &&
        header.value.startsWith('admin_auth=')
    );
  expect(cookie.value).toMatch(/secure/i);
  expect(cookie.value).toMatch(/httponly/i);
  expect(cookie.value).toMatch(/samesite=lax/i);
  expect((await request.get('/admin/rooms')).ok()).toBeTruthy();
  expect(
    (
      await request.post('/admin/logout', {
        headers: await csrf(request),
        maxRedirects: 0
      })
    ).status()
  ).toBe(302);
  expect((await request.get('/admin/rooms')).status()).toBe(401);
});

test('foreign browser websocket origin is rejected', async ({ request }) => {
  const response = await request.get('/roomHub', {
    headers: {
      Origin: 'https://foreign.example',
      Upgrade: 'websocket',
      Connection: 'Upgrade'
    }
  });
  expect(response.status()).toBe(403);
});
