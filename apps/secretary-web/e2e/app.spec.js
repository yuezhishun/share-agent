import { expect, test } from '@playwright/test';

function installMockRuntime(page) {
  return page.addInitScript(() => {
    const state = {
      nextId: 2,
      invokes: [],
      uploads: [],
      instances: [
        {
          id: 'mock-1',
          command: 'bash',
          cwd: '/home/yueyuan/demo',
          cols: 80,
          rows: 25,
          status: 'running',
          created_at: new Date().toISOString(),
          clients: 0,
          node_id: 'master-mock',
          node_name: 'Master Mock',
          node_role: 'master',
          node_online: true
        },
        {
          id: 'slave-a-1',
          command: 'bash',
          cwd: '/srv/slave-a',
          cols: 100,
          rows: 30,
          status: 'running',
          created_at: new Date().toISOString(),
          clients: 0,
          node_id: 'slave-a',
          node_name: 'Slave A',
          node_role: 'slave',
          node_online: true,
          node_label: 'region-a'
        },
        {
          id: 'slave-b-1',
          command: 'bash',
          cwd: '/srv/slave-b',
          cols: 120,
          rows: 40,
          status: 'running',
          created_at: new Date().toISOString(),
          clients: 0,
          node_id: 'slave-b',
          node_name: 'Slave B',
          node_role: 'slave',
          node_online: false,
          node_label: 'region-b'
        }
      ],
      wsInputs: [],
      files: {
        '/home/yueyuan': [
          { name: 'demo', path: '/home/yueyuan/demo', kind: 'dir', size: null, mtime: new Date().toISOString() },
          { name: 'readme.txt', path: '/home/yueyuan/readme.txt', kind: 'file', size: 24, mtime: new Date().toISOString() }
        ],
        '/home/yueyuan/demo': [
          { name: 'main.js', path: '/home/yueyuan/demo/main.js', kind: 'file', size: 32, mtime: new Date().toISOString() }
        ]
      }
    };

    const json = (payload, status = 200) => new Response(JSON.stringify(payload), {
      status,
      headers: { 'content-type': 'application/json' }
    });

    const text = (payload, status = 200) => new Response(payload, {
      status,
      headers: { 'content-type': 'text/plain' }
    });

    globalThis.__PW_MOCK_STATE__ = state;

    const nativeFetch = globalThis.fetch.bind(globalThis);
    globalThis.fetch = async (input, init = {}) => {
      const reqUrl = typeof input === 'string' ? input : input?.url || '';
      const url = new URL(reqUrl, globalThis.location.origin);
      const method = String(init.method || 'GET').toUpperCase();
      const pathname = url.pathname;

      const isApiPath = pathname.startsWith('/web-pty/api/') || pathname.startsWith('/api/');
      if (!isApiPath) {
        return nativeFetch(input, init);
      }

      const normalizedPath = pathname.replace(/^\/web-pty/, '');

      if (normalizedPath === '/api/instances' && method === 'GET') {
        return json({ items: state.instances });
      }

      if (normalizedPath === '/api/projects' && method === 'GET') {
        return json({
          base: '/home/yueyuan',
          items: [
            { name: 'demo', path: '/home/yueyuan/demo' },
            { name: 'repo', path: '/home/yueyuan/repo' }
          ]
        });
      }

      if (normalizedPath === '/api/nodes' && method === 'GET') {
        return json({
          items: [
            {
              node_id: 'master-mock',
              node_name: 'Master Mock',
              node_role: 'master',
              node_online: true,
              instance_count: state.instances.length,
              last_seen_at: new Date().toISOString()
            },
            {
              node_id: 'slave-a',
              node_name: 'Slave A',
              node_role: 'slave',
              node_online: true,
              node_label: 'region-a',
              instance_count: state.instances.filter((x) => x.node_id === 'slave-a').length,
              last_seen_at: new Date().toISOString()
            },
            {
              node_id: 'slave-b',
              node_name: 'Slave B',
              node_role: 'slave',
              node_online: false,
              node_label: 'region-b',
              instance_count: state.instances.filter((x) => x.node_id === 'slave-b').length,
              last_seen_at: new Date().toISOString()
            }
          ]
        });
      }

      if (normalizedPath === '/api/instances' && method === 'POST') {
        const body = init.body ? JSON.parse(String(init.body)) : {};
        const id = `mock-${state.nextId++}`;
        const created = {
          id,
          command: String(body.command || 'bash'),
          cwd: String(body.cwd || '/home/yueyuan'),
          cols: Number(body.cols || 80),
          rows: Number(body.rows || 25),
          status: 'running',
          created_at: new Date().toISOString(),
          clients: 0,
          node_id: 'master-mock',
          node_name: 'Master Mock',
          node_role: 'master',
          node_online: true
        };
        state.instances.unshift(created);
        return json({
          instance_id: id,
          node_id: 'master-mock',
          hub_url: `${globalThis.location.origin}/web-pty/hubs/terminal`
        });
      }

      if (/^\/api\/nodes\/[^/]+\/instances$/.test(normalizedPath) && method === 'POST') {
        const body = init.body ? JSON.parse(String(init.body)) : {};
        const nodeId = decodeURIComponent(normalizedPath.split('/')[3] || '');
        const nodeName = nodeId === 'slave-a' ? 'Slave A' : nodeId === 'slave-b' ? 'Slave B' : 'Master Mock';
        const nodeRole = nodeId === 'master-mock' ? 'master' : 'slave';
        const id = `${nodeId}-new-${state.nextId++}`;
        state.instances.unshift({
          id,
          command: String(body.command || 'bash'),
          cwd: String(body.cwd || '/tmp'),
          cols: Number(body.cols || 80),
          rows: Number(body.rows || 25),
          status: 'running',
          created_at: new Date().toISOString(),
          clients: 0,
          node_id: nodeId,
          node_name: nodeName,
          node_role: nodeRole,
          node_online: nodeId !== 'slave-b'
        });
        return json({
          instance_id: id,
          node_id: nodeId,
          hub_url: `${globalThis.location.origin}/web-pty/hubs/terminal`
        });
      }

      if (/^\/api\/nodes\/[^/]+\/files\/upload$/.test(normalizedPath) && method === 'POST') {
        const nodeId = decodeURIComponent(normalizedPath.split('/')[3] || '');
        const form = init.body;
        const instanceId = form?.get ? String(form.get('instance_id') || '') : '';
        const path = `/tmp/${nodeId}-upload-${Date.now()}.png`;
        state.uploads.push({ nodeId, instanceId, path });
        return json({
          node_id: nodeId,
          instance_id: instanceId,
          upload: { path, size: 11 }
        });
      }

      if (/^\/api\/instances\/.+$/.test(normalizedPath) && method === 'DELETE') {
        const id = decodeURIComponent(normalizedPath.split('/').pop() || '');
        state.instances = state.instances.filter((x) => x.id !== id);
        return json({ ok: true });
      }

      if (normalizedPath === '/api/files/list' && method === 'GET') {
        const path = url.searchParams.get('path') || '/home/yueyuan';
        const items = state.files[path] || [];
        const parent = path === '/home/yueyuan' ? null : '/home/yueyuan';
        return json({
          base: '/home/yueyuan',
          path,
          parent,
          items
        });
      }

      if (normalizedPath === '/api/files/read' && method === 'GET') {
        const path = url.searchParams.get('path') || '/home/yueyuan/readme.txt';
        return json({
          path,
          encoding: 'utf-8',
          size: 24,
          content: `mock file content: ${path}`,
          lines_shown: 1,
          max_lines: 500,
          truncated: false,
          truncate_reason: null,
          byte_limit: 1024 * 1024
        });
      }

      return text(`unhandled mock api: ${normalizedPath} ${method}`, 500);
    };

    class MockHubConnection {
      constructor() {
        this.handlers = new Map();
        this.state = 'Disconnected';
        this.instanceId = 'mock-1';
        this.reconnectingHandler = null;
        this.reconnectedHandler = null;
        this.closeHandler = null;
      }

      on(event, handler) {
        this.handlers.set(event, handler);
      }

      async start() {
        this.state = 'Connected';
      }

      async stop() {
        this.state = 'Disconnected';
        this.closeHandler?.();
      }

      onreconnecting(handler) {
        this.reconnectingHandler = handler;
      }

      onreconnected(handler) {
        this.reconnectedHandler = handler;
      }

      onclose(handler) {
        this.closeHandler = handler;
      }

      async invoke(method, payload = {}) {
        state.invokes.push({ method, payload });

        if (method === 'JoinInstance') {
          this.instanceId = String(payload.instanceId || 'mock-1');
          const handler = this.handlers.get('TerminalEvent');
          handler?.({
            v: 1,
            type: 'term.snapshot',
            instance_id: this.instanceId,
            seq: 1,
            ts: Date.now(),
            size: { cols: 80, rows: 25 },
            cursor: { x: 0, y: 0, visible: true },
            styles: { '0': {} },
            rows: [{ y: 0, segs: [['mock ready', 0]] }],
            history: { available: 0, newest_cursor: 'h-1' }
          });
          return;
        }

        if (method === 'RequestSync') {
          const handler = this.handlers.get('TerminalEvent');
          handler?.({
            v: 1,
            type: 'term.snapshot',
            instance_id: String(payload.instanceId || this.instanceId),
            seq: 9,
            ts: Date.now(),
            size: { cols: 80, rows: 25 },
            cursor: { x: 0, y: 0, visible: true },
            styles: { '0': {} },
            rows: [{ y: 0, segs: [['resynced', 0]] }],
            history: { available: 0, newest_cursor: 'h-1' }
          });
          return;
        }

        if (method === 'SendInput') {
          const targetId = String(payload.instanceId || this.instanceId);
          state.wsInputs.push(String(payload.data || ''));
          const handler = this.handlers.get('TerminalEvent');
          handler?.({
            v: 1,
            type: 'term.patch',
            instance_id: targetId,
            seq: 2,
            ts: Date.now(),
            cursor: { x: 0, y: 1, visible: true },
            styles: { '0': {} },
            rows: [{ y: 1, segs: [[`echo:${String(payload.data || '').trim()}`, 0]] }]
          });
        }
      }
    }

    class MockHubConnectionBuilder {
      withUrl() {
        return this;
      }

      withAutomaticReconnect() {
        return this;
      }

      configureLogging() {
        return this;
      }

      build() {
        const conn = new MockHubConnection();
        state.hubConnection = conn;
        return conn;
      }
    }

    globalThis.__WEBCLI_SIGNALR_BUILDER__ = () => new MockHubConnectionBuilder();
  });
}

test('desktop can create and connect instance with mock backend', async ({ page }) => {
  await installMockRuntime(page);
  await page.goto('/');

  await expect(page.getByRole('heading', { name: 'WebCLI Desktop' })).toBeVisible();
  await page.getByTestId('create-button').click();

  await expect(page.getByTestId('status')).toContainText('Connected');
  await expect(page.getByTestId('plain-output')).toContainText('resynced');
  await expect(page.locator('#instance-list li')).toHaveCount(4);
});

test('mobile terminal sends shortcut input through mocked websocket', async ({ page }) => {
  await installMockRuntime(page);
  await page.goto('/mobile');

  await page.selectOption('select', 'mock-1');
  await page.getByRole('button', { name: 'Connect', exact: true }).click();
  await expect(page.getByText('Connected')).toBeVisible();

  for (const name of ['Esc', 'Tab', 'Enter', 'Ctrl+C', '↑', '↓', '←', '→']) {
    await page.getByRole('button', { name }).click();
  }
  await expect
    .poll(async () => page.evaluate(() => globalThis.__PW_MOCK_STATE__.wsInputs))
    .toEqual(expect.arrayContaining(['\u001b', '\t', '\r', '\u0003', '\u001b[A', '\u001b[B', '\u001b[D', '\u001b[C']));
});

test('desktop paste should send bracketed payload', async ({ page }) => {
  await installMockRuntime(page);
  await page.goto('/');

  await page.getByRole('button', { name: 'Connect', exact: true }).first().click();
  await page.getByTestId('terminal').click();
  await page.evaluate(() => {
    const terminal = document.querySelector('[data-testid="terminal"]');
    if (!terminal) {
      return;
    }
    const event = new Event('paste', { bubbles: true, cancelable: true });
    Object.defineProperty(event, 'clipboardData', {
      value: {
        getData: () => 'echo one\necho two'
      }
    });
    terminal.dispatchEvent(event);
  });

  await expect
    .poll(async () => page.evaluate(() => globalThis.__PW_MOCK_STATE__.wsInputs))
    .toContain('\u001b[200~echo one\necho two\u001b[201~');
});

test('desktop upload image supports insert and insert+enter', async ({ page }) => {
  await installMockRuntime(page);
  await page.goto('/');

  await page.getByRole('button', { name: 'Connect', exact: true }).first().click();
  const uploadInput = page.locator('input[type="file"]');

  await uploadInput.setInputFiles({
    name: 'terminal.png',
    mimeType: 'image/png',
    buffer: Buffer.from('png-content')
  });
  await expect
    .poll(async () => page.evaluate(() => globalThis.__PW_MOCK_STATE__.wsInputs.at(-1)))
    .toContain('/tmp/');

  await page.getByTestId('upload-mode-select').selectOption('insert_enter');
  await uploadInput.setInputFiles({
    name: 'terminal.png',
    mimeType: 'image/png',
    buffer: Buffer.from('png-content-2')
  });
  await expect
    .poll(async () => page.evaluate(() => globalThis.__PW_MOCK_STATE__.wsInputs.at(-1)))
    .toMatch(/\/tmp\/.*\r$/);
});

test('desktop create routes to selected node and auto-resyncs on seq gap', async ({ page }) => {
  await installMockRuntime(page);
  await page.goto('/');

  await page.getByTestId('node-select').selectOption('slave-a');
  await page.getByTestId('create-button').click();

  await expect(page.getByTestId('status')).toContainText('Connected');
  await expect(page.locator('#instance-list li').first()).toContainText('Slave A');
  await expect(page.locator('#instance-list li', { hasText: 'offline' }).first()).toBeVisible();

  const before = await page.evaluate(() => globalThis.__PW_MOCK_STATE__.invokes.filter((x) => x.method === 'RequestSync').length);

  await page.evaluate(() => {
    const conn = globalThis.__PW_MOCK_STATE__.hubConnection;
    const handler = conn?.handlers?.get?.('TerminalEvent');
    if (handler) {
      handler({
        v: 1,
        type: 'term.route',
        instance_id: globalThis.__PW_MOCK_STATE__.instances[0].id,
        reason: 'seq_gap',
        action: 'resync_requested',
        node_id: 'slave-a',
        node_name: 'Slave A'
      });
    }
  });

  await expect
    .poll(async () => page.evaluate(() => globalThis.__PW_MOCK_STATE__.invokes.filter((x) => x.method === 'RequestSync').length))
    .toBeGreaterThan(before);
});

test('mobile files route can list and preview files with mock backend', async ({ page }) => {
  await installMockRuntime(page);
  await page.goto('/mobile/files');

  await expect(page.getByRole('heading', { name: 'Mobile Files' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'file · readme.txt' })).toBeVisible();

  await page.getByRole('button', { name: 'file · readme.txt' }).click();
  await expect(page.locator('pre.preview')).toContainText('mock file content: /home/yueyuan/readme.txt');
});
