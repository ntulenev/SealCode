import { test, expect } from '@playwright/test';

test('Chromium exposes webdriver for the automation hooks', async ({ page }) => {
  await page.goto('/');
  expect(await page.evaluate(() => navigator.webdriver)).toBe(true);
});
