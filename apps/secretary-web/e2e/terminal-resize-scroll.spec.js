import { expect, test } from '@playwright/test';
import { installMockRuntime } from './support/mock-runtime.js';

test('desktop should keep output stable when resize ack/snapshot and scrolling happen together', async ({ page }) => {
  await installMockRuntime(page);
  await page.goto('/');

  await page.locator('#instance-list .terminal-item').first().click();
  await expect(page.getByTestId('status')).toContainText('Connected');

  for (let i = 0; i < 20; i += 1) {
    await page.getByTestId('terminal').click();
    await page.keyboard.type(`line-${i}`);
    await page.keyboard.press('Enter');
  }

  await expect(page.getByTestId('plain-output')).toContainText('line-19');

  await page.evaluate(() => {
    const conn = globalThis.__PW_MOCK_STATE__.hubConnection;
    conn.emit('TerminalEvent', {
      v: 1,
      type: 'term.resize.ack',
      instance_id: 'mock-1',
      req_id: 'resize-test',
      size: { cols: 120, rows: 40 },
      ts: Date.now()
    });
    conn.emit('TerminalEvent', {
      v: 1,
      type: 'term.raw',
      instance_id: 'mock-1',
      replay: true,
      req_id: 'resize-replay',
      to_seq: 999,
      seq: 999,
      reset: false,
      ts: Date.now(),
      data: 'after-resize-a\\r\\nafter-resize-b\\r\\n'
    });
  });

  await expect(page.getByTestId('plain-output')).toContainText('after-resize-a');
  await expect(page.getByTestId('plain-output')).toContainText('after-resize-b');
});

test('desktop should recompute terminal geometry after viewport width changes', async ({ page }) => {
  await installMockRuntime(page);
  await page.setViewportSize({ width: 1680, height: 980 });
  await page.goto('/');

  await page.locator('#instance-list .terminal-item').first().click();
  await expect(page.getByTestId('status')).toContainText('Connected');

  const before = await page.evaluate(() => globalThis.__PW_MOCK_STATE__.resizeRequests.length);

  await page.setViewportSize({ width: 1260, height: 980 });

  await expect
    .poll(async () => page.evaluate(() => globalThis.__PW_MOCK_STATE__.resizeRequests.length))
    .toBeGreaterThan(before);
});

test('desktop seq gap route should auto trigger resync request', async ({ page }) => {
  await installMockRuntime(page);
  await page.goto('/');

  await page.locator('#instance-list .terminal-item').first().click();
  const before = await page.evaluate(() => globalThis.__PW_MOCK_STATE__.invokes.filter((x) => x.method === 'RequestSync').length);

  await page.evaluate(() => globalThis.__PW_MOCK_STATE__.hubConnection.emitSeqGap());
  await expect(page.getByTestId('status')).toContainText('Resync requested');

  await expect
    .poll(async () => page.evaluate(() => globalThis.__PW_MOCK_STATE__.invokes.filter((x) => x.method === 'RequestSync').length))
    .toBeGreaterThan(before);
});

test('desktop should auto join all instances and sync selected instance from server baseline', async ({ page }) => {
  await installMockRuntime(page);
  await page.goto('/');

  await page.locator('#instance-list .terminal-item').first().click();
  await expect(page.getByTestId('status')).toContainText('Connected');

  await page.getByTestId('create-button').click();
  await expect(page.locator('#instance-list .terminal-item')).toHaveCount(2);

  await page.locator('#instance-list .terminal-item').nth(0).click();
  await expect(page.getByTestId('status')).toContainText('Connected');

  await expect.poll(async () => page.evaluate(() => {
    const joined = globalThis.__PW_MOCK_STATE__.joinedInstanceIds || [];
    return joined.slice().sort();
  })).toEqual(['mock-1', 'mock-2']);

  await page.evaluate(() => {
    globalThis.__PW_MOCK_STATE__.emitServerRaw('mock-1', 'background-log-1\\r\\n');
  });

  const beforeSwitchSyncCount = await page.evaluate(
    () => globalThis.__PW_MOCK_STATE__.invokes.filter((x) => x.method === 'RequestSync').length
  );

  await page.locator('#instance-list .terminal-item').nth(1).click();
  await expect(page.getByTestId('status')).toContainText('Connected');
  await expect(page.getByTestId('plain-output')).toContainText('background-log-1');

  const afterSwitchSyncCount = await page.evaluate(
    () => globalThis.__PW_MOCK_STATE__.invokes.filter((x) => x.method === 'RequestSync').length
  );
  expect(afterSwitchSyncCount).toBe(beforeSwitchSyncCount + 1);
});

test('desktop should always request server baseline sync when switching to a newly discovered instance', async ({ page }) => {
  await installMockRuntime(page);
  await page.goto('/');

  await page.locator('#instance-list .terminal-item').first().click();
  await expect(page.getByTestId('status')).toContainText('Connected');

  const beforeSyncCount = await page.evaluate(
    () => globalThis.__PW_MOCK_STATE__.invokes.filter((x) => x.method === 'RequestSync').length
  );

  await page.evaluate(() => {
    const state = globalThis.__PW_MOCK_STATE__;
    state.instances.unshift({
      id: 'mock-3',
      command: 'codex --dangerously-bypass-approvals-and-sandbox',
      cwd: '/home/yueyuan/new',
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
    state.screenByInstance['mock-3'] = {
      cols: 80,
      rows: 25,
      lines: ['booting'],
      seq: 1,
      inputBuffer: '',
      rawChunks: [{ seq: 1, data: 'booting\\r\\n' }]
    };
  });

  await page.locator('#refreshTerminalIcon').click();
  await expect(page.locator('#instance-list .terminal-item')).toHaveCount(2);

  await page.evaluate(() => {
    globalThis.__PW_MOCK_STATE__.emitServerRaw('mock-3', 'welcome-fragment\\r\\n');
  });

  await page.locator('#instance-list .terminal-item').nth(0).click();
  await expect(page.getByTestId('status')).toContainText('Connected');
  await expect(page.getByTestId('plain-output')).toContainText('welcome-fragment');

  const afterSyncCount = await page.evaluate(
    () => globalThis.__PW_MOCK_STATE__.invokes.filter((x) => x.method === 'RequestSync').length
  );
  expect(afterSyncCount).toBeGreaterThan(beforeSyncCount);
});
