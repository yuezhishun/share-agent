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

test('desktop switch between two instances should use incremental raw sync after warm-up', async ({ page }) => {
  await installMockRuntime(page);
  await page.goto('/');

  await page.locator('#instance-list .terminal-item').first().click();
  await expect(page.getByTestId('status')).toContainText('Connected');

  await page.getByTestId('create-button').click();
  await expect(page.locator('#instance-list .terminal-item')).toHaveCount(2);

  await page.locator('#instance-list .terminal-item').nth(1).click();
  await expect(page.getByTestId('status')).toContainText('Connected');

  const latestSyncSinceSeq = await page.evaluate(() => {
    const syncs = globalThis.__PW_MOCK_STATE__.invokes.filter((x) => x.method === 'RequestSync');
    const last = syncs.at(-1);
    return Number(last?.payload?.sinceSeq || 0);
  });

  expect(latestSyncSinceSeq).toBeGreaterThan(0);
});
