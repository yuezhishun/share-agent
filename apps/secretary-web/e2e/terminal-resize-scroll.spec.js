import { expect, test } from '@playwright/test';
import { installMockRuntime } from './support/mock-runtime.js';

test('desktop should keep output stable when resize ack/snapshot and scrolling happen together', async ({ page }) => {
  await installMockRuntime(page);
  await page.goto('/');

  await page.getByRole('button', { name: 'Connect', exact: true }).first().click();
  await expect(page.getByTestId('status')).toContainText('Connected:');

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
      type: 'term.snapshot',
      instance_id: 'mock-1',
      seq: 100,
      ts: Date.now(),
      size: { cols: 120, rows: 40 },
      cursor: { x: 0, y: 1, visible: true },
      styles: { '0': {} },
      rows: [
        { y: 0, segs: [['after-resize-a', 0]] },
        { y: 1, segs: [['after-resize-b', 0]] }
      ],
      history: { available: 0, newest_cursor: 'h-1' }
    });
  });

  await expect(page.getByTestId('plain-output')).toContainText('after-resize-a');
  await expect(page.getByTestId('plain-output')).toContainText('after-resize-b');
});

test('desktop seq gap route should auto trigger resync request', async ({ page }) => {
  await installMockRuntime(page);
  await page.goto('/');

  await page.getByRole('button', { name: 'Connect', exact: true }).first().click();
  const before = await page.evaluate(() => globalThis.__PW_MOCK_STATE__.invokes.filter((x) => x.method === 'RequestSync').length);

  await page.evaluate(() => globalThis.__PW_MOCK_STATE__.hubConnection.emitSeqGap());
  await expect(page.getByTestId('status')).toContainText('Resync requested:');

  await expect
    .poll(async () => page.evaluate(() => globalThis.__PW_MOCK_STATE__.invokes.filter((x) => x.method === 'RequestSync').length))
    .toBeGreaterThan(before);
});
