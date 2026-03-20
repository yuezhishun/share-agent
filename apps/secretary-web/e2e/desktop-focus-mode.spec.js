import { expect, test } from '@playwright/test';
import { installMockRuntime } from './support/mock-runtime.js';

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

  await page.getByTestId('terminal').click();
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
