import { expect, test } from '@playwright/test';

test('home page renders navigation', async ({ page }) => {
  await page.goto('/');

  await expect(page.getByRole('heading', { name: 'AI Secretary Console' })).toBeVisible();
  await expect(page.getByRole('link', { name: '任务看板' })).toBeVisible();
  await expect(page.getByRole('link', { name: '终端工作台' })).toBeVisible();
});

test('can navigate to terminal workspace', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('link', { name: '终端工作台' }).click();

  await expect(page).toHaveURL(/\/terminal/);
  await expect(page.getByRole('button', { name: '新建' })).toBeVisible();
});

test('terminal paste works with Ctrl+V, right-click and paste button', async ({ page, context }) => {
  await context.grantPermissions(['clipboard-read', 'clipboard-write']);

  await page.addInitScript(() => {
    const state = {
      sessions: [],
      wsInputPayloads: []
    };

    const makeJsonResponse = (body, status = 200) => new Response(JSON.stringify(body), {
      status,
      headers: { 'content-type': 'application/json' }
    });

    globalThis.__PW_TERMINAL_STATE__ = state;
    globalThis.__PW_DISABLE_CLIPBOARD_READ__ = false;

    const nativeFetch = globalThis.fetch.bind(globalThis);
    globalThis.fetch = async (input, init = {}) => {
      const reqUrl = typeof input === 'string' ? input : input?.url || '';
      const url = new URL(reqUrl, globalThis.location.origin);
      const method = String(init.method || 'GET').toUpperCase();

      if (url.pathname === '/terminal-api/profiles' && method === 'GET') {
        return makeJsonResponse([]);
      }

      if (url.pathname === '/terminal-api/sessions' && method === 'GET') {
        return makeJsonResponse(state.sessions);
      }

      if (url.pathname === '/terminal-api/sessions' && method === 'POST') {
        const body = init.body ? JSON.parse(String(init.body)) : {};
        const sessionId = `pw-session-${Date.now()}`;
        const created = {
          sessionId,
          taskId: body.taskId || 'pw-task',
          title: body.title || 'pw-terminal',
          profileId: body.profileId || null,
          cliType: 'custom',
          mode: 'execute',
          cwd: '/tmp',
          shell: '/bin/bash',
          args: [],
          status: 'running',
          writable: true,
          cols: Number(body.cols || 160),
          rows: Number(body.rows || 40),
          writeToken: 'pw-write-token'
        };
        state.sessions = [created];
        return makeJsonResponse(created);
      }

      if (/^\/terminal-api\/sessions\/[^/]+\/terminate$/.test(url.pathname) && method === 'POST') {
        return makeJsonResponse({ ok: true });
      }

      return nativeFetch(input, init);
    };

    class MockWebSocket {
      static CONNECTING = 0;
      static OPEN = 1;
      static CLOSING = 2;
      static CLOSED = 3;

      constructor(url) {
        this.url = String(url);
        this.readyState = MockWebSocket.CONNECTING;
        this.onopen = null;
        this.onmessage = null;
        this.onerror = null;
        this.onclose = null;

        const parsed = new URL(this.url);
        const sessionId = parsed.searchParams.get('sessionId') || '';

        setTimeout(() => {
          this.readyState = MockWebSocket.OPEN;
          this.onopen?.({ type: 'open' });
          this.onmessage?.({
            data: JSON.stringify({
              type: 'ready',
              sessionId,
              taskId: 'pw-task',
              status: 'running',
              writable: true,
              cols: 160,
              rows: 40
            })
          });
        }, 0);
      }

      send(raw) {
        const payload = JSON.parse(String(raw));
        if (payload?.type === 'input') {
          state.wsInputPayloads.push(String(payload.data || ''));
        }
      }

      close() {
        this.readyState = MockWebSocket.CLOSED;
        this.onclose?.({ type: 'close' });
      }
    }

    globalThis.WebSocket = MockWebSocket;

    const clipboard = globalThis.navigator?.clipboard;
    if (clipboard?.readText) {
      const originalReadText = clipboard.readText.bind(clipboard);
      clipboard.readText = async () => {
        if (globalThis.__PW_DISABLE_CLIPBOARD_READ__) {
          throw new Error('clipboard read disabled for fallback test');
        }
        return originalReadText();
      };
    }
  });

  await page.goto('/terminal');
  await page.getByRole('button', { name: '新建' }).click();
  await expect(page.getByText('连接 connected')).toBeVisible();

  const terminal = page.locator('.terminal-wrap').first();

  const ctrlVText = `pw-ctrl-v-${Date.now()}\n`;
  await page.evaluate((text) => navigator.clipboard.writeText(text), ctrlVText);
  await terminal.click();
  await page.keyboard.press('Control+v');
  await expect.poll(async () => page.evaluate(() => globalThis.__PW_TERMINAL_STATE__.wsInputPayloads)).toContain(ctrlVText);

  const contextMenuPrevented = await terminal.evaluate((el) => {
    const event = new MouseEvent('contextmenu', { bubbles: true, cancelable: true });
    return !el.dispatchEvent(event);
  });
  expect(contextMenuPrevented).toBeTruthy();

  await page.evaluate(() => {
    globalThis.__PW_DISABLE_CLIPBOARD_READ__ = true;
  });

  const buttonText = `pw-button-${Date.now()}\n`;
  await page.evaluate((text) => navigator.clipboard.writeText(text), buttonText);
  await page.getByRole('button', { name: '粘贴' }).click();
  await expect(page.getByText('剪贴板读取受限，请按 Ctrl/Cmd+V 完成粘贴')).toBeVisible();
  await page.evaluate((text) => {
    const bridge = document.querySelector('.terminal-paste-bridge');
    if (!bridge) {
      throw new Error('paste bridge not found');
    }
    const dt = new DataTransfer();
    dt.setData('text', text);
    bridge.dispatchEvent(new ClipboardEvent('paste', { bubbles: true, cancelable: true, clipboardData: dt }));
  }, buttonText);
  await expect.poll(async () => page.evaluate(() => globalThis.__PW_TERMINAL_STATE__.wsInputPayloads)).toContain(buttonText);
});
