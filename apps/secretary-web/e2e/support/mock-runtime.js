export function installMockRuntime(page) {
  return page.addInitScript(() => {
    const state = {
      nextId: 2,
      invokes: [],
      wsInputs: [],
      resizeRequests: [],
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
        }
      ],
      screenByInstance: {
        'mock-1': {
          cols: 80,
          rows: 25,
          lines: ['mock ready'],
          seq: 1
        }
      }
    };

    const json = (payload, status = 200) => new Response(JSON.stringify(payload), {
      status,
      headers: { 'content-type': 'application/json' }
    });

    globalThis.__PW_MOCK_STATE__ = state;

    const nativeFetch = globalThis.fetch.bind(globalThis);
    globalThis.fetch = async (input, init = {}) => {
      const reqUrl = typeof input === 'string' ? input : input?.url || '';
      const url = new URL(reqUrl, globalThis.location.origin);
      const method = String(init.method || 'GET').toUpperCase();
      const pathname = url.pathname.replace(/^\/web-pty/, '');

      if (!pathname.startsWith('/api/')) {
        return nativeFetch(input, init);
      }

      if (pathname === '/api/instances' && method === 'GET') {
        return json({ items: state.instances });
      }

      if (pathname === '/api/nodes' && method === 'GET') {
        return json({
          items: [
            {
              node_id: 'master-mock',
              node_name: 'Master Mock',
              node_role: 'master',
              node_online: true,
              instance_count: state.instances.length,
              last_seen_at: new Date().toISOString()
            }
          ]
        });
      }

      if (pathname === '/api/projects' && method === 'GET') {
        return json({ base: '/home/yueyuan', items: [{ name: 'demo', path: '/home/yueyuan/demo' }] });
      }

      if (pathname === '/api/instances' && method === 'POST') {
        const body = init.body ? JSON.parse(String(init.body)) : {};
        const id = `mock-${state.nextId++}`;
        const cols = Number(body.cols || 80);
        const rows = Number(body.rows || 25);
        state.instances.unshift({
          id,
          command: String(body.command || 'bash'),
          cwd: String(body.cwd || '/home/yueyuan'),
          cols,
          rows,
          status: 'running',
          created_at: new Date().toISOString(),
          clients: 0,
          node_id: 'master-mock',
          node_name: 'Master Mock',
          node_role: 'master',
          node_online: true
        });
        state.screenByInstance[id] = { cols, rows, lines: ['mock ready'], seq: 1 };
        return json({ instance_id: id, node_id: 'master-mock', hub_url: `${globalThis.location.origin}/hubs/terminal` });
      }

      if (pathname.startsWith('/api/instances/') && method === 'DELETE') {
        const id = decodeURIComponent(pathname.split('/').pop() || '');
        state.instances = state.instances.filter((x) => x.id !== id);
        delete state.screenByInstance[id];
        return json({ ok: true });
      }

      if (pathname.startsWith('/api/nodes/') && pathname.endsWith('/files/upload') && method === 'POST') {
        return json({ node_id: 'master-mock', instance_id: 'mock-1', upload: { path: '/tmp/mock-upload.png', size: 11 } });
      }

      return new Response(`unhandled mock api: ${pathname} ${method}`, { status: 500 });
    };

    function getScreen(instanceId) {
      const key = String(instanceId || 'mock-1');
      if (!state.screenByInstance[key]) {
        state.screenByInstance[key] = { cols: 80, rows: 25, lines: ['mock ready'], seq: 1, inputBuffer: '' };
      }
      return state.screenByInstance[key];
    }

    function toSnapshot(instanceId) {
      const screen = getScreen(instanceId);
      screen.seq += 1;
      return {
        v: 1,
        type: 'term.snapshot',
        instance_id: instanceId,
        seq: screen.seq,
        ts: Date.now(),
        size: { cols: screen.cols, rows: screen.rows },
        cursor: { x: 0, y: Math.max(0, screen.lines.length - 1), visible: true },
        styles: { '0': {} },
        rows: screen.lines.map((line, y) => ({ y, segs: [[line, 0]] })),
        history: { available: 0, newest_cursor: 'h-1' }
      };
    }

    function appendInput(instanceId, data) {
      const screen = getScreen(instanceId);
      const body = String(data || '');
      for (const ch of body) {
        if (ch === '\r' || ch === '\n') {
          if (screen.inputBuffer.length > 0) {
            screen.lines.push(`echo:${screen.inputBuffer}`);
            screen.inputBuffer = '';
          }
        } else {
          screen.inputBuffer += ch;
        }
      }
      if (screen.lines.length > screen.rows) {
        screen.lines = screen.lines.slice(screen.lines.length - screen.rows);
      }
      screen.seq += 1;
      return {
        v: 1,
        type: 'term.patch',
        instance_id: instanceId,
        seq: screen.seq,
        ts: Date.now(),
        cursor: { x: 0, y: Math.max(0, screen.lines.length - 1), visible: true },
        styles: { '0': {} },
        rows: screen.lines.map((line, y) => ({ y, segs: [[line, 0]] }))
      };
    }

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

      emit(event, payload) {
        this.handlers.get(event)?.(payload);
      }

      triggerReconnecting() {
        this.reconnectingHandler?.(new Error('mock reconnecting'));
      }

      async triggerReconnected() {
        this.state = 'Connected';
        await this.reconnectedHandler?.('mock-reconnected');
      }

      triggerClose() {
        this.state = 'Disconnected';
        this.closeHandler?.(new Error('mock close'));
      }

      emitSeqGap() {
        this.emit('TerminalEvent', {
          v: 1,
          type: 'term.route',
          instance_id: this.instanceId,
          reason: 'seq_gap',
          action: 'resync_requested',
          node_id: 'master-mock',
          node_name: 'Master Mock'
        });
      }

      async invoke(method, payload = {}) {
        state.invokes.push({ method, payload });

        if (method === 'JoinInstance') {
          this.instanceId = String(payload.instanceId || 'mock-1');
          this.emit('TerminalEvent', toSnapshot(this.instanceId));
          return;
        }

        if (method === 'RequestSync') {
          const id = String(payload.instanceId || this.instanceId);
          this.emit('TerminalEvent', toSnapshot(id));
          return;
        }

        if (method === 'SendInput') {
          state.wsInputs.push(String(payload.data || ''));
          this.emit('TerminalEvent', appendInput(this.instanceId, payload.data));
          return;
        }

        if (method === 'RequestResize') {
          const id = String(payload.instanceId || this.instanceId);
          const screen = getScreen(id);
          screen.cols = Number(payload.cols || screen.cols);
          screen.rows = Number(payload.rows || screen.rows);
          state.resizeRequests.push({ cols: screen.cols, rows: screen.rows });
          this.emit('TerminalEvent', {
            v: 1,
            type: 'term.resize.ack',
            instance_id: id,
            req_id: String(payload.reqId || ''),
            size: { cols: screen.cols, rows: screen.rows },
            ts: Date.now()
          });
          this.emit('TerminalEvent', toSnapshot(id));
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
