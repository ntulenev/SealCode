import { test, expect } from './fixtures.js';
import { csrf } from './admin-token.js';

test('suggested name can be accepted immediately or replaced with a custom name', async ({ page, request }, testInfo) => {
  const created = await request.post('/admin/rooms', {
    headers: await csrf(request), data: { name: 'Suggested names', language: 'csharp' }
  });
  expect(created.ok()).toBeTruthy();
  const roomId = (await created.json()).RoomId;
  try {
    await page.goto(`/room/${roomId}`);
    const input = page.locator('#displayNameInput');
    await expect(input).toBeEnabled();
    await expect(input).toHaveValue(/^[A-Z][a-z]+ [A-Z][a-z]+$/);
    const regenerate = page.getByRole('button', { name: 'Generate another name' });
    await expect(regenerate).toBeEnabled();
    for (let attempt = 0; attempt < 3; attempt++) {
      const previous = await input.inputValue();
      await regenerate.click();
      await expect(input).not.toHaveValue(previous);
      await expect(input).toHaveValue(/^[A-Z][a-z]+ [A-Z][a-z]+$/);
      await expect(input).toBeFocused();
      await expect(page.locator('#joinOverlay')).toBeVisible();
    }
    const suggested = await input.inputValue();
    expect(suggested.length).toBeLessThanOrEqual(20);
    await page.screenshot({ path: testInfo.outputPath('suggested-name.png') });
    await page.setViewportSize({ width: 375, height: 812 });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: testInfo.outputPath('suggested-name-mobile.png') });
    await expect(page.locator('#joinOverlay')).toBeVisible();
    await input.press('Enter');
    await expect(page.locator('#joinOverlay')).toBeHidden();
    await expect(page.locator('#participantsList')).toContainText(suggested);

    await page.reload();
    await expect(input).toBeEnabled();
    await expect(input).toHaveValue(/^[A-Z][a-z]+ [A-Z][a-z]+$/);
    await input.fill('Иван Иванов');
    await page.locator('#joinBtn').click();
    await expect(page.locator('#joinOverlay')).toBeHidden();
    await expect(page.locator('#participantsList')).toContainText('Иван Иванов');
  } finally {
    await request.delete(`/admin/rooms/${roomId}`, { headers: await csrf(request) });
  }
});
