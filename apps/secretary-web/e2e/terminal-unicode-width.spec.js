import { expect, test } from '@playwright/test';
import { installMockRuntime } from './support/mock-runtime.js';

test('desktop snapshot and patch should preserve unicode and wide-char content ordering', async ({ page }) => {
  await installMockRuntime(page);
  await page.goto('/');

  await page.locator('#instance-list .terminal-item').first().click();
  await expect(page.getByTestId('status')).toContainText('Connected');

  await page.evaluate(() => {
    const conn = globalThis.__PW_MOCK_STATE__.hubConnection;
    conn.emit('TerminalEvent', {
      v: 1,
      type: 'term.raw',
      instance_id: 'mock-1',
      replay: true,
      req_id: 'unicode-replay',
      to_seq: 999,
      seq: 999,
      reset: false,
      ts: Date.now(),
      data: 'ASCII hello\\r\\n全角字符\\r\\ne\u0301 + 😀\\r\\n补丁行：宽字符😀终止\\r\\n'
    });
  });

  await expect(page.getByTestId('plain-output')).toContainText('ASCII hello');
  await expect(page.getByTestId('plain-output')).toContainText('全角字符');
  await expect(page.getByTestId('plain-output')).toContainText('é + 😀');
  await expect(page.getByTestId('plain-output')).toContainText('补丁行：宽字符😀终止');
});
