import { expect, test } from '@playwright/test';
import { installMockRuntime } from './support/mock-runtime.js';

test('mobile reconnect should show status, disable connect button, and auto rejoin+resync', async ({ page }) => {
  await installMockRuntime(page);
  await page.goto('/mobile');

  await page.selectOption('select', 'mock-1');
  const connectBtn = page.getByRole('button', { name: 'Connect', exact: true });
  await connectBtn.click();
  await expect(page.getByText('Connected')).toBeVisible();

  await page.evaluate(() => globalThis.__PW_MOCK_STATE__.hubConnection.triggerReconnecting());
  await expect(page.getByText('Reconnecting...')).toBeVisible();
  await expect(connectBtn).toBeDisabled();

  const beforeJoin = await page.evaluate(() => globalThis.__PW_MOCK_STATE__.invokes.filter((x) => x.method === 'JoinInstance').length);
  const beforeSync = await page.evaluate(() => globalThis.__PW_MOCK_STATE__.invokes.filter((x) => x.method === 'RequestSync').length);

  await page.evaluate(async () => {
    await globalThis.__PW_MOCK_STATE__.hubConnection.triggerReconnected();
  });

  await expect(page.getByText('Connected')).toBeVisible();
  await expect
    .poll(async () => page.evaluate(() => ({
      join: globalThis.__PW_MOCK_STATE__.invokes.filter((x) => x.method === 'JoinInstance').length,
      sync: globalThis.__PW_MOCK_STATE__.invokes.filter((x) => x.method === 'RequestSync').length
    })))
    .toEqual({ join: beforeJoin + 1, sync: beforeSync + 1 });

  await page.evaluate(() => globalThis.__PW_MOCK_STATE__.hubConnection.triggerClose());
  await expect(page.getByText('disconnected')).toBeVisible();
  await expect(connectBtn).toBeEnabled();
});

test('desktop connect buttons should be disabled during reconnecting and restored after recovery', async ({ page }) => {
  await installMockRuntime(page);
  await page.goto('/');

  await page.locator('#instance-list .terminal-item').first().click();
  await expect(page.getByTestId('status')).toContainText('Connected');

  await page.evaluate(() => globalThis.__PW_MOCK_STATE__.hubConnection.triggerReconnecting());
  await expect(page.getByTestId('status')).toContainText('Reconnecting...');

  const beforeJoin = await page.evaluate(() => globalThis.__PW_MOCK_STATE__.invokes.filter((x) => x.method === 'JoinInstance').length);
  const beforeSync = await page.evaluate(() => globalThis.__PW_MOCK_STATE__.invokes.filter((x) => x.method === 'RequestSync').length);
  await page.evaluate(async () => {
    await globalThis.__PW_MOCK_STATE__.hubConnection.triggerReconnected();
  });

  await expect(page.getByTestId('status')).toContainText('Connected');
  await expect
    .poll(async () => page.evaluate(() => ({
      join: globalThis.__PW_MOCK_STATE__.invokes.filter((x) => x.method === 'JoinInstance').length,
      sync: globalThis.__PW_MOCK_STATE__.invokes.filter((x) => x.method === 'RequestSync').length
    })))
    .toEqual({ join: beforeJoin + 1, sync: beforeSync + 1 });
});
