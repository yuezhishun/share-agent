import { expect, test } from '@playwright/test';
import { installMockRuntime } from './support/mock-runtime.js';

async function focusTerminalInput(page) {
  await page.locator('.xterm-helper-textarea').waitFor({ state: 'attached' });
  await page.evaluate(() => {
    const helper = document.querySelector('.xterm-helper-textarea');
    if (!(helper instanceof HTMLTextAreaElement)) {
      throw new Error('xterm helper textarea not found');
    }
    helper.focus();
  });
}

test('desktop can collapse left/right sidebars independently without losing terminal input', async ({ page }) => {
  await installMockRuntime(page);
  await page.goto('/');

  const leftToggle = page.getByTestId('toggle-left-sidebar');
  const rightToggle = page.getByTestId('toggle-right-sidebar');
  const leftSidebar = page.locator('.left-sidebar');
  const rightSidebar = page.locator('.right-sidebar');
  const main = page.locator('.main');

  await expect(leftToggle).toContainText('隐藏左栏');
  await expect(rightToggle).toContainText('隐藏右栏');
  await expect(leftSidebar).toBeVisible();
  await expect(rightSidebar).toBeVisible();
  await expect(main).not.toHaveClass(/left-collapsed/);
  await expect(main).not.toHaveClass(/right-collapsed/);
  await expect(main).not.toHaveClass(/both-collapsed/);

  await leftToggle.click();
  await expect(leftToggle).toContainText('显示左栏');
  await expect(main).toHaveClass(/left-collapsed/);
  await expect(main).not.toHaveClass(/right-collapsed/);
  await expect(leftSidebar).toBeHidden();
  await expect(rightSidebar).toBeVisible();

  await focusTerminalInput(page);
  await page.keyboard.type('pwd');
  await page.keyboard.press('Enter');

  await expect
    .poll(async () => page.evaluate(() => globalThis.__PW_MOCK_STATE__.wsInputs))
    .toEqual(expect.arrayContaining(['p', 'w', 'd', '\r']));

  await expect
    .poll(async () => page.evaluate(() => String(document.activeElement?.className || '')))
    .toContain('xterm-helper-textarea');

  await rightToggle.click();
  await expect(rightToggle).toContainText('显示右栏');
  await expect(main).toHaveClass(/both-collapsed/);
  await expect(leftSidebar).toBeHidden();
  await expect(rightSidebar).toBeHidden();

  await leftToggle.click();
  await expect(leftToggle).toContainText('隐藏左栏');
  await expect(main).toHaveClass(/right-collapsed/);
  await expect(main).not.toHaveClass(/both-collapsed/);
  await expect(leftSidebar).toBeVisible();
  await expect(rightSidebar).toBeHidden();

  await rightToggle.click();
  await expect(rightToggle).toContainText('隐藏右栏');
  await expect(main).not.toHaveClass(/left-collapsed/);
  await expect(main).not.toHaveClass(/right-collapsed/);
  await expect(main).not.toHaveClass(/both-collapsed/);
  await expect(leftSidebar).toBeVisible();
  await expect(rightSidebar).toBeVisible();
});

test('desktop view should auto-collapse both sidebars on phone viewport', async ({ page }) => {
  await installMockRuntime(page);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/');

  const leftToggle = page.getByTestId('toggle-left-sidebar');
  const rightToggle = page.getByTestId('toggle-right-sidebar');
  const leftSidebar = page.locator('.left-sidebar');
  const rightSidebar = page.locator('.right-sidebar');
  const main = page.locator('.main');

  await expect(leftToggle).toContainText('显示左栏');
  await expect(rightToggle).toContainText('显示右栏');
  await expect(main).toHaveClass(/both-collapsed/);
  await expect(leftSidebar).toBeHidden();
  await expect(rightSidebar).toBeHidden();
});

test('desktop gutters should collapse and expand sidebars independently', async ({ page }) => {
  await installMockRuntime(page);
  await page.goto('/');

  const leftGutter = page.getByTestId('left-sidebar-gutter');
  const rightGutter = page.getByTestId('right-sidebar-gutter');
  const leftSidebar = page.locator('.left-sidebar');
  const rightSidebar = page.locator('.right-sidebar');
  const main = page.locator('.main');

  await leftGutter.click();
  await expect(main).toHaveClass(/left-collapsed/);
  await expect(leftSidebar).toBeHidden();
  await expect(rightSidebar).toBeVisible();

  await leftGutter.click();
  await expect(main).not.toHaveClass(/left-collapsed/);
  await expect(leftSidebar).toBeVisible();
  await expect(rightSidebar).toBeVisible();

  await rightGutter.click();
  await expect(main).toHaveClass(/right-collapsed/);
  await expect(leftSidebar).toBeVisible();
  await expect(rightSidebar).toBeHidden();

  await rightGutter.click();
  await expect(main).not.toHaveClass(/right-collapsed/);
  await expect(leftSidebar).toBeVisible();
  await expect(rightSidebar).toBeVisible();
});

test('desktop gutters should support keyboard toggle without losing terminal input', async ({ page }) => {
  await installMockRuntime(page);
  await page.goto('/');

  const leftGutter = page.getByTestId('left-sidebar-gutter');

  await focusTerminalInput(page);
  await leftGutter.focus();
  await page.keyboard.press('Enter');
  await expect(page.locator('.main')).toHaveClass(/left-collapsed/);
  await expect
    .poll(async () => page.evaluate(() => String(document.activeElement?.className || '')))
    .toContain('xterm-helper-textarea');
});

test('desktop file browser should expose top-row file actions and create folders inline', async ({ page }) => {
  await installMockRuntime(page);
  await page.goto('/');

  const hiddenToggle = page.getByTestId('toggle-hidden-files');
  const createFolderToggle = page.getByTestId('create-folder-toggle');
  const uploadTrigger = page.getByTestId('upload-file-trigger');

  await expect(hiddenToggle).toBeVisible();
  await expect(createFolderToggle).toBeVisible();
  await expect(uploadTrigger).toBeVisible();

  await createFolderToggle.click();
  await expect(page.getByTestId('folder-creator')).toBeVisible();
  await page.getByTestId('folder-name-input').fill('workspace');
  await page.getByRole('button', { name: '创建' }).click();

  await expect(page.getByTestId('folder-creator')).toBeHidden();
  await expect(page.locator('#fileList .file-item').filter({ hasText: 'workspace' })).toHaveCount(1);
});

test('desktop should focus terminal after creating or reselecting a session from another center tab', async ({ page }) => {
  await installMockRuntime(page);
  await page.goto('/');

  await page.locator('#instance-list .terminal-item').first().hover();
  await page.locator('#instance-list .terminal-item .rename-btn[title="查看纯文本"]').first().click();
  await expect(page.locator('.terminal-tabs .file-tab').first()).toHaveClass(/active/);

  await page.locator('#instance-list .terminal-item').first().click();
  await expect
    .poll(async () => page.evaluate(() => String(document.activeElement?.className || '')))
    .toContain('xterm-helper-textarea');

  await page.getByTestId('create-button').click();
  await expect(page.locator('#instance-list .terminal-item')).toHaveCount(2);
  await expect
    .poll(async () => page.evaluate(() => String(document.activeElement?.className || '')))
    .toContain('xterm-helper-textarea');
});

test('desktop creators should focus the first input automatically', async ({ page }) => {
  await installMockRuntime(page);
  await page.goto('/');

  await page.getByTestId('create-folder-toggle').click();
  await expect(page.getByTestId('folder-creator')).toBeVisible();
  await expect
    .poll(async () => page.evaluate(() => document.activeElement?.getAttribute('data-testid') || ''))
    .toBe('folder-name-input');

  await page.getByRole('button', { name: '快捷指令' }).click();
  await page.locator('#addShortcutIcon').click();
  await expect(page.getByTestId('shortcut-editor')).toBeVisible();
  await expect(page.locator('.shortcut-editor input').first()).toBeFocused();

  await page.getByRole('button', { name: '终端配方' }).click();
  await page.locator('#addRecipeIcon').click();
  await expect(page.locator('.recipe-editor')).toBeVisible();
  await expect(page.locator('.recipe-editor input').first()).toBeFocused();
});

test.fixme('desktop terminal should accept synthetic IME beforeinput text from helper textarea', async ({ page }) => {
  await installMockRuntime(page);
  await page.goto('/');

  await focusTerminalInput(page);

  await page.evaluate(() => {
    const helper = document.querySelector('.xterm-helper-textarea');
    if (!(helper instanceof HTMLTextAreaElement)) {
      throw new Error('xterm helper textarea not found');
    }

    helper.dispatchEvent(new InputEvent('beforeinput', {
      bubbles: true,
      cancelable: true,
      data: '语音输入',
      inputType: 'insertFromComposition'
    }));
  });

  await expect
    .poll(async () => page.evaluate(() => globalThis.__PW_MOCK_STATE__.wsInputs))
    .toEqual(expect.arrayContaining(['语音输入']));
  await expect
    .poll(async () => page.evaluate(() => String(document.activeElement?.className || '')))
    .toContain('xterm-helper-textarea');
});

test('desktop shortcut sidebar should toggle voice mode', async ({ page }) => {
  await installMockRuntime(page);
  await page.goto('/');

  await page.getByRole('button', { name: '快捷指令' }).click();

  const modeToggle = page.getByTestId('voice-mode-toggle');
  const voiceInput = page.getByTestId('voice-mode-input');
  const status = page.getByTestId('status');
  const settingsCard = page.getByTestId('voice-mode-settings');

  await expect(settingsCard).toContainText('网页快捷键：Ctrl+↓');
  await expect(settingsCard).toContainText('当前输入：普通终端输入');

  await modeToggle.click();
  await expect(status).toContainText('语音模式已开启');
  await expect(settingsCard).toContainText('当前输入：语音输入模式');
  await expect
    .poll(async () => page.evaluate(() => document.activeElement?.getAttribute('data-testid') || ''))
    .toBe('voice-mode-input');

  await expect(voiceInput).toBeAttached();

  await modeToggle.click();
  await expect(status).toContainText('语音模式已关闭');
  await expect(settingsCard).toContainText('当前输入：普通终端输入');
});

test('desktop terminal should toggle voice mode on Ctrl+ArrowDown', async ({ page }) => {
  await installMockRuntime(page);
  await page.goto('/');

  const status = page.getByTestId('status');

  await focusTerminalInput(page);
  await page.keyboard.press('ArrowDown');
  await expect(status).not.toContainText('语音模式已开启');

  await page.keyboard.press('Control+ArrowDown');
  await expect(status).toContainText('语音模式已开启');
  await expect
    .poll(async () => page.evaluate(() => document.activeElement?.getAttribute('data-testid') || ''))
    .toBe('voice-mode-input');

  await page.keyboard.press('Control+ArrowDown');
  await expect(status).toContainText('语音模式已关闭');
  await expect
    .poll(async () => page.evaluate(() => String(document.activeElement?.className || '')))
    .toContain('xterm-helper-textarea');
});
