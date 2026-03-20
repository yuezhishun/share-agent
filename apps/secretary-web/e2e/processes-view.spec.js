import { expect, test } from '@playwright/test';
import { installMockRuntime } from './support/mock-runtime.js';

test('processes view should switch nodes and manage remote processes', async ({ page }) => {
  await installMockRuntime(page);
  await page.goto('/proc');

  await page.locator('.node-select').selectOption('slave-mock');
  await expect(page.locator('.proc-list .proc-item').first()).toContainText('proc-2');

  await page.locator('.command-input').fill('bash -lc "echo slave process"');
  await page.getByTitle('运行').click();

  await expect(page.locator('.proc-list .proc-item').first()).toContainText('running');
  await expect(page.locator('.output-entry-content').first()).toContainText('started on slave-mock');

  await page.getByTitle('停止进程').click();
  await expect(page.locator('.summary-item').filter({ hasText: '节点' })).toContainText(/slave-mock|Slave Mock/);
});
