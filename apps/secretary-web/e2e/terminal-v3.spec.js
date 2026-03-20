import { expect, test } from '@playwright/test';
import { installMockRuntime } from './support/mock-runtime.js';

test('terminal v3 should keep desktop collapse behavior without losing terminal input', async ({ page }) => {
  await installMockRuntime(page);
  await page.goto('/terminal-v3');

  const leftToggle = page.getByTestId('toggle-left-sidebar');
  const rightToggle = page.getByTestId('toggle-right-sidebar');
  const main = page.locator('.main');

  await expect(page.getByTestId('status')).toContainText('Connected');
  await expect(main).not.toHaveClass(/both-collapsed/);

  await leftToggle.click();
  await expect(main).toHaveClass(/left-collapsed/);

  await page.getByTestId('terminal').click();
  await page.keyboard.type('pwd');
  await page.keyboard.press('Enter');

  await expect
    .poll(async () => page.evaluate(() => globalThis.__PW_MOCK_STATE__.wsInputs))
    .toEqual(expect.arrayContaining(['p', 'w', 'd', '\r']));

  await rightToggle.click();
  await expect(main).toHaveClass(/both-collapsed/);
});

test('terminal v3 instance alias should persist after reload', async ({ page }) => {
  await installMockRuntime(page);
  await page.goto('/terminal-v3');

  await page.locator('#instance-list .terminal-item').first().hover();
  await page.getByTestId('rename-instance-mock-1').click();
  await page.getByTestId('rename-instance-input-mock-1').fill('构建机 shell');
  await page.getByTestId('rename-instance-input-mock-1').press('Enter');

  await expect(page.locator('#instance-list .terminal-item').first()).toContainText('构建机 shell');
  await expect(page.locator('#currentTerminalName')).toContainText('构建机 shell');

  await page.reload();

  await expect(page.locator('#instance-list .terminal-item').first()).toContainText('构建机 shell');
  await expect(page.locator('#currentTerminalName')).toContainText('构建机 shell');
});

test('terminal v3 paste should stay bracketed and file browser should open editable tabs', async ({ page }) => {
  await page.addInitScript(() => {
    globalThis.__PW_CLIPBOARD__ = 'echo one\necho two';
    Object.defineProperty(globalThis.navigator, 'clipboard', {
      configurable: true,
      value: {
        writeText: async (text) => {
          globalThis.__PW_CLIPBOARD__ = String(text || '');
        },
        readText: async () => String(globalThis.__PW_CLIPBOARD__ || '')
      }
    });
  });
  await installMockRuntime(page);
  await page.goto('/terminal-v3');

  await page.getByTestId('terminal').click();
  await page.evaluate(() => {
    const terminal = document.querySelector('[data-testid="terminal"]');
    terminal?.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, button: 2 }));
  });

  await expect
    .poll(async () => page.evaluate(() => globalThis.__PW_MOCK_STATE__.wsInputs.at(-1)))
    .toBe('\u001b[200~echo one\necho two\u001b[201~');

  await page.locator('#fileList .file-item', { hasText: 'readme.txt' }).click();
  await expect(page.locator('.tab-btn.file-tab')).toContainText('readme.txt');
  await expect(page.locator('.editor-textarea')).toHaveValue('hello from mock file\n');

  await page.locator('.editor-textarea').fill('updated from playwright\n');
  await page.getByRole('button', { name: '保存' }).click();
  await expect(page.getByTestId('status')).toContainText('Saved: /home/yueyuan/demo/readme.txt');
});

test('terminal v3 should create and manage node-scoped processes and expose recipes in right tabs', async ({ page }) => {
  await installMockRuntime(page);
  await page.goto('/terminal-v3');

  await page.locator('.node-target .node-select').selectOption('slave-mock');
  await page.locator('.process-command-input').fill('bash -lc "echo slave"');
  await page.getByRole('button', { name: '运行' }).click();

  await expect(page.locator('.process-list .process-item').first()).toContainText('proc-');
  await expect(page.locator('.process-output-content')).toContainText('started on slave-mock');

  await page.locator('.right-tab-btn', { hasText: '终端配方' }).click();
  await expect(page.locator('#recipeList')).toBeVisible();
});
