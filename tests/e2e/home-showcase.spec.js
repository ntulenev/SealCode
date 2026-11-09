import { test, expect } from '@playwright/test';

test('product homepage presents all four workflows in both themes and narrow layouts', async ({ page }, testInfo) => {
  const errors = [];
  page.on('pageerror', (error) => errors.push(error.message));
  page.on('console', (message) => { if (message.type() === 'error') errors.push(message.text()); });
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.goto('/');
  await expect(page.getByRole('heading', { level: 1 })).toHaveCount(1);
  for (const theme of ['dark', 'light']) {
    if (await page.locator('html').getAttribute('data-theme') !== theme)
      await page.locator('[data-theme-toggle]').click();
    for (const width of [1440, 390, 320]) {
      await page.setViewportSize({ width, height: 900 });
      for (const id of ['code', 'whiteboard', 'rooms', 'snippets']) {
        const section = page.locator(`section#${id}`);
        await section.scrollIntoViewIfNeeded();
        await expect(section.getByRole('heading')).toBeVisible();
        const image = section.locator('a[data-screenshot]:visible img');
        await expect(image).toBeVisible();
        expect(await image.evaluate(async (img) => {
          await img.decode();
          return img.naturalWidth > 0;
        })).toBe(true);
        if (id === 'code' || id === 'whiteboard') await expect(image).toHaveAttribute('src', new RegExp(`-${theme}\\.png`));
        if (id === 'snippets') await expect(image).toHaveAttribute('src', new RegExp(theme === 'light' ? 'snippets-tab-light\\.png' : 'snippets-tab\\.png'));
      }
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      await page.screenshot({ path: testInfo.outputPath(`home-${theme}-${width}.png`), fullPage: true, animations: 'disabled' });
    }
  }
  expect(errors).toEqual([]);
});

test('screenshots enlarge with the keyboard, preserve focus and support native link modifiers', async ({ page }, testInfo) => {
  await page.goto('/');
  await page.getByRole('link', { name: 'See what you can do' }).click();
  await expect(page).toHaveURL(/#explore$/);
  const dialog = page.getByRole('dialog', { name: 'Product screenshot' });
  for (const id of ['code', 'whiteboard', 'rooms', 'snippets']) {
    const preview = page.locator(`#${id} a[data-screenshot]:visible`);
    await preview.focus();
    await page.keyboard.press('Enter');
    await expect(dialog).toBeVisible();
    await expect(dialog.locator('img')).toHaveAttribute('src', await preview.locator('img').evaluate((img) => img.src));
    await expect(dialog.getByRole('button', { name: 'Close screenshot' })).toBeFocused();
    await page.keyboard.press('Escape');
    await expect(dialog).toBeHidden();
    await expect(preview).toBeFocused();
  }
  const preview = page.locator('#code a[data-screenshot]:visible');
  await preview.click();
  await dialog.getByRole('button', { name: 'Close screenshot' }).click();
  await expect(dialog).toBeHidden();
  // Modified clicks retain browser behavior rather than trapping the user in a modal.
  await preview.dispatchEvent('click', { ctrlKey: true });
  await expect(dialog).toBeHidden();
  await page.locator('[data-theme-toggle]').click();
  const lightSnippets = page.locator('#snippets a[data-screenshot]:visible');
  await lightSnippets.click();
  await expect(dialog.locator('img')).toHaveAttribute('src', /snippets-tab-light\.png/);
  await page.keyboard.press('Escape');
  await page.setViewportSize({ width: 390, height: 844 });
  await preview.click();
  await expect(dialog).toBeVisible();
  await dialog.evaluate((el) => { el.scrollTop = el.scrollHeight; el.scrollLeft = 300; });
  await expect(dialog.getByRole('button', { name: 'Close screenshot' })).toBeInViewport();
  await page.screenshot({ path: testInfo.outputPath('screenshot-mobile.png') });
  await dialog.getByRole('button', { name: 'Close screenshot' }).click();
  await expect(dialog).toBeHidden();
});

test('homepage content and invitation links remain usable without JavaScript', async ({ browser, baseURL }) => {
  const context = await browser.newContext({ javaScriptEnabled: false, baseURL });
  try {
    const page = await context.newPage();
    await page.goto('/');
    await expect(page.locator('.product-feature')).toHaveCount(4);
    const preview = page.locator('#code a[data-screenshot]:visible');
    await preview.click();
    await expect(page).toHaveURL(/\/product\/room-dark\.png/);
    await page.goto('/');
    await page.locator('.home-cta').getByRole('link', { name: 'Already have an invitation?' }).click();
    await expect(page.locator('#roomLink')).toBeFocused();
    await page.locator('.home-cta').getByRole('link', { name: 'Create a room' }).click();
    await expect(page).toHaveURL(/\/admin\/login$/);
  } finally { await context.close(); }
});
