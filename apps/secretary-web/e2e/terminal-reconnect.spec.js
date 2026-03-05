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

test('desktop reconnect should rejoin all instances but only backfill selected instance', async ({ page }) => {
  await installMockRuntime(page);
  await page.goto('/');

  await page.locator('#instance-list .terminal-item').first().click();
  await expect(page.getByTestId('status')).toContainText('Connected');

  await page.getByTestId('create-button').click();
  await expect(page.locator('#instance-list .terminal-item')).toHaveCount(2);
  await page.locator('#instance-list .terminal-item').nth(0).click();
  await expect(page.getByTestId('status')).toContainText('Connected');

  const before = await page.evaluate(() => ({
    join: globalThis.__PW_MOCK_STATE__.invokes.filter((x) => x.method === 'JoinInstance').length,
    sync: globalThis.__PW_MOCK_STATE__.invokes.filter((x) => x.method === 'RequestSync').length
  }));

  await page.evaluate(() => globalThis.__PW_MOCK_STATE__.hubConnection.triggerReconnecting());
  await expect(page.getByTestId('status')).toContainText('Reconnecting...');
  await page.evaluate(async () => {
    await globalThis.__PW_MOCK_STATE__.hubConnection.triggerReconnected();
  });

  await expect(page.getByTestId('status')).toContainText('Connected');
  await expect
    .poll(async () => page.evaluate(() => {
      const invokes = globalThis.__PW_MOCK_STATE__.invokes || [];
      return {
        join: invokes.filter((x) => x.method === 'JoinInstance').length,
        sync: invokes.filter((x) => x.method === 'RequestSync').length
      };
    }))
    .toEqual({ join: before.join + 2, sync: before.sync + 1 });

  const syncTargetIds = await page.evaluate(() => {
    const invokes = globalThis.__PW_MOCK_STATE__.invokes || [];
    return invokes
      .filter((x) => x.method === 'RequestSync')
      .slice(-1)
      .map((x) => String(x?.payload?.instanceId || ''));
  });
  expect(syncTargetIds).toEqual(['mock-2']);
});

test('desktop connect should tolerate stale background join failure and still connect selected instance', async ({ page }) => {
  await installMockRuntime(page);
  await page.goto('/');

  await page.locator('#instance-list .terminal-item').first().click();
  await expect(page.getByTestId('status')).toContainText('Connected');

  await page.evaluate(() => {
    const state = globalThis.__PW_MOCK_STATE__;
    state.instances.unshift({
      id: 'stale-1',
      command: 'bash',
      cwd: '/stale',
      cols: 80,
      rows: 25,
      status: 'running',
      created_at: new Date().toISOString(),
      clients: 0,
      node_id: 'master-mock',
      node_name: 'Master Mock',
      node_role: 'master',
      node_online: true
    });

    const conn = state.hubConnection;
    if (!conn.__patchedJoinFailure) {
      const originalInvoke = conn.invoke.bind(conn);
      state.joinAttemptCounters = state.joinAttemptCounters || {};
      conn.invoke = async (method, payload = {}) => {
        if (method === 'JoinInstance') {
          const targetId = String(payload.instanceId || '');
          state.joinAttemptCounters[targetId] = Number(state.joinAttemptCounters[targetId] || 0) + 1;
          if (targetId === 'stale-1') {
            throw new Error('stale instance');
          }
        }
        return originalInvoke(method, payload);
      };
      conn.__patchedJoinFailure = true;
    }
  });

  await page.locator('#refreshTerminalIcon').click();
  await expect(page.getByTestId('status')).toContainText('Connected');

  const beforeReconnected = await page.evaluate(() => ({
    stale: Number(globalThis.__PW_MOCK_STATE__.joinAttemptCounters?.['stale-1'] || 0),
    selected: Number(globalThis.__PW_MOCK_STATE__.joinAttemptCounters?.['mock-1'] || 0)
  }));

  await page.evaluate(() => globalThis.__PW_MOCK_STATE__.hubConnection.triggerReconnecting());
  await expect(page.getByTestId('status')).toContainText('Reconnecting...');
  await page.evaluate(async () => {
    await globalThis.__PW_MOCK_STATE__.hubConnection.triggerReconnected();
  });

  await expect(page.getByTestId('status')).toContainText('Connected');
  await expect
    .poll(async () => page.evaluate(() => {
      return {
        stale: Number(globalThis.__PW_MOCK_STATE__.joinAttemptCounters?.['stale-1'] || 0),
        selected: Number(globalThis.__PW_MOCK_STATE__.joinAttemptCounters?.['mock-1'] || 0)
      };
    }))
    .toEqual({ stale: beforeReconnected.stale + 1, selected: beforeReconnected.selected + 1 });
});
