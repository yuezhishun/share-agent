import { expect, test } from '@playwright/test';
import { installMockRuntime } from './support/mock-runtime.js';

test('terminal v2 should connect and request a fresh screen snapshot after reconnect', async ({ page }) => {
  await installMockRuntime(page);
  await page.goto('/');

  await page.locator('#instance-list .terminal-item').first().click();
  await expect(page.getByTestId('status')).toContainText('Connected');

  const beforeSync = await page.evaluate(() => globalThis.__PW_MOCK_STATE__.invokes.filter((item) => item.method === 'RequestSync').length);
  await page.evaluate(() => globalThis.__PW_MOCK_STATE__.hubConnection.triggerReconnecting());
  await expect(page.getByTestId('status')).toContainText('Reconnecting...');

  await page.evaluate(async () => {
    await globalThis.__PW_MOCK_STATE__.hubConnection.triggerReconnected();
  });

  await expect(page.getByTestId('status')).toContainText('Connected');
  await expect
    .poll(async () => page.evaluate(() => globalThis.__PW_MOCK_STATE__.invokes.filter((item) => item.method === 'RequestSync').length))
    .toBeGreaterThan(beforeSync);
});

test('desktop terminal should request a fresh screen sync when the document becomes visible', async ({ page }) => {
  await installMockRuntime(page);
  await page.goto('/');

  await page.locator('#instance-list .terminal-item').first().click();
  await expect(page.getByTestId('status')).toContainText('Connected');

  const beforeSync = await page.evaluate(() => globalThis.__PW_MOCK_STATE__.invokes.filter((item) => item.method === 'RequestSync').length);
  await page.evaluate(() => {
    Object.defineProperty(document, 'hidden', { configurable: true, value: false });
    document.dispatchEvent(new Event('visibilitychange'));
  });

  await expect
    .poll(async () => page.evaluate(() => globalThis.__PW_MOCK_STATE__.invokes.filter((item) => item.method === 'RequestSync').length))
    .toBeGreaterThan(beforeSync);
});
