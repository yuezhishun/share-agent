import { expect, test } from '@playwright/test';
import { installMockRuntime } from './support/mock-runtime.js';

test('desktop snapshot and patch should preserve unicode and wide-char content ordering', async ({ page }) => {
  await installMockRuntime(page);
  await page.goto('/');

  await page.getByRole('button', { name: 'Connect', exact: true }).first().click();
  await expect(page.getByTestId('status')).toContainText('Connected:');

  await page.evaluate(() => {
    const conn = globalThis.__PW_MOCK_STATE__.hubConnection;
    conn.emit('TerminalEvent', {
      v: 1,
      type: 'term.snapshot',
      instance_id: 'mock-1',
      seq: 200,
      ts: Date.now(),
      size: { cols: 80, rows: 25 },
      cursor: { x: 0, y: 2, visible: true },
      styles: { '0': {} },
      rows: [
        { y: 0, segs: [['ASCII hello', 0]] },
        { y: 1, segs: [['全角字符', 0]] },
        { y: 2, segs: [['e\u0301 + 😀', 0]] }
      ],
      history: { available: 0, newest_cursor: 'h-1' }
    });

    conn.emit('TerminalEvent', {
      v: 1,
      type: 'term.patch',
      instance_id: 'mock-1',
      seq: 201,
      ts: Date.now(),
      cursor: { x: 0, y: 3, visible: true },
      styles: { '0': {} },
      rows: [
        { y: 3, segs: [['补丁行：宽字符😀终止', 0]] }
      ]
    });
  });

  await expect(page.getByTestId('plain-output')).toContainText('ASCII hello');
  await expect(page.getByTestId('plain-output')).toContainText('全角字符');
  await expect(page.getByTestId('plain-output')).toContainText('é + 😀');
  await expect(page.getByTestId('plain-output')).toContainText('补丁行：宽字符😀终止');
});
