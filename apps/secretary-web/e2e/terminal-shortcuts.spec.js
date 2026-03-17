import { expect, test } from '@playwright/test';
import { installMockRuntime } from './support/mock-runtime.js';

test('desktop instance alias should persist after page reload', async ({ page }) => {
  await installMockRuntime(page);
  await page.goto('/');

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

test('desktop ctrl+c should copy selection instead of sending interrupt when terminal text is selected', async ({ page }) => {
  await page.addInitScript(() => {
    globalThis.__PW_CLIPBOARD__ = '';
    Object.defineProperty(globalThis.navigator, 'clipboard', {
      configurable: true,
      value: {
        writeText: async (text) => {
          globalThis.__PW_CLIPBOARD__ = String(text || '');
        },
        readText: async () => String(globalThis.__PW_CLIPBOARD__ || '')
      }
    });
    globalThis.__WEBCLI_DESKTOP_TERM_HOOK__ = (term) => {
      globalThis.__PW_DESKTOP_TERM__ = term;
    };
  });
  await installMockRuntime(page);
  await page.goto('/');

  await page.locator('#instance-list .terminal-item').first().click();
  await page.getByTestId('terminal').click();
  await expect
    .poll(async () => page.evaluate(() => !!globalThis.__PW_DESKTOP_TERM__))
    .toBe(true);

  await page.evaluate(() => {
    globalThis.__PW_DESKTOP_TERM__.reset();
    globalThis.__PW_DESKTOP_TERM__.write('copy me');
    globalThis.__PW_DESKTOP_TERM__.select(0, 0, 7);
    globalThis.__PW_DESKTOP_TERM__.focus();
  });

  const beforeCount = await page.evaluate(() => globalThis.__PW_MOCK_STATE__.wsInputs.length);
  await page.keyboard.press('Control+C');

  await expect
    .poll(async () => page.evaluate(() => globalThis.__PW_CLIPBOARD__))
    .toBe('copy me');
  await expect
    .poll(async () => page.evaluate(() => globalThis.__PW_MOCK_STATE__.wsInputs.length))
    .toBe(beforeCount);
});

test('mobile shortcut keys should keep mapping, focus, and remain stable under burst clicks', async ({ page }) => {
  await installMockRuntime(page);
  await page.goto('/mobile');

  await page.selectOption('select', 'mock-1');
  await page.getByRole('button', { name: 'Connect', exact: true }).click();
  await expect(page.getByText('Connected')).toBeVisible();

  const mapping = [
    ['Esc', '\u001b'],
    ['Tab', '\t'],
    ['Enter', '\r'],
    ['Ctrl+C', '\u0003'],
    ['↑', '\u001b[A'],
    ['↓', '\u001b[B'],
    ['←', '\u001b[D'],
    ['→', '\u001b[C']
  ];

  for (const [name] of mapping) {
    for (let i = 0; i < 20; i += 1) {
      await page.getByRole('button', { name }).click();
      await expect
        .poll(async () => page.evaluate(() => {
          const active = document.activeElement;
          return {
            className: String(active?.className || ''),
            inTerminal: !!active?.closest?.('.mobile-terminal')
          };
        }))
        .toEqual(expect.objectContaining({
          className: expect.stringContaining('xterm-helper-textarea'),
          inTerminal: true
        }));
    }
  }

  await expect
    .poll(async () => page.evaluate(() => globalThis.__PW_MOCK_STATE__.wsInputs))
    .toEqual(expect.arrayContaining(mapping.map(([, value]) => value)));
});

test('desktop paste should stay bracketed after mixed websocket input activity', async ({ page }) => {
  await installMockRuntime(page);
  await page.goto('/');

  await page.locator('#instance-list .terminal-item').first().click();
  await page.getByTestId('terminal').click();
  await page.keyboard.type('echo before-paste');
  await page.keyboard.press('Enter');

  await page.evaluate(() => {
    const terminal = document.querySelector('[data-testid="terminal"]');
    const evt = new Event('paste', { bubbles: true, cancelable: true });
    Object.defineProperty(evt, 'clipboardData', {
      value: { getData: () => 'echo one\necho two' }
    });
    terminal?.dispatchEvent(evt);
  });

  await expect
    .poll(async () => page.evaluate(() => globalThis.__PW_MOCK_STATE__.wsInputs.at(-1)))
    .toBe('\u001b[200~echo one\necho two\u001b[201~');
});
