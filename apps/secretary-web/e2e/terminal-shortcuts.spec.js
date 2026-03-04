import { expect, test } from '@playwright/test';
import { installMockRuntime } from './support/mock-runtime.js';

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
