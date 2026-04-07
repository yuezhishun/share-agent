export function installMockRuntime(page) {
  return page.addInitScript(() => {
    const state = {
      nextId: 2,
      nextProcessId: 2,
      invokes: [],
      wsInputs: [],
      resizeRequests: [],
      joinedInstanceIds: [],
      nodes: [
        {
          node_id: 'master-mock',
          node_name: 'Master Mock',
          node_role: 'master',
          node_online: true
        },
        {
          node_id: 'slave-mock',
          node_name: 'Slave Mock',
          node_role: 'slave',
          node_online: true
        }
      ],
      files: {
        '/home': { kind: 'dir' },
        '/home/yueyuan': { kind: 'dir' },
        '/home/yueyuan/demo': { kind: 'dir' },
        '/home/yueyuan/demo/readme.txt': { kind: 'file', content: 'hello from mock file\n' },
        '/home/yueyuan/demo/script.sh': { kind: 'file', content: 'echo mock\n' }
      },
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
          seq: 1,
          inputBuffer: '',
          rawChunks: [{ seq: 1, data: 'mock ready\r\n' }]
        }
      },
      processesByNode: {
        'master-mock': [
          {
            processId: 'proc-1',
            status: 'running',
            startTime: new Date().toISOString(),
            endTime: null,
            durationMs: 800,
            command: 'bash -lc "npm run dev"',
            outputCount: 2,
            metadata: { source: 'processes-view' },
            result: null
          }
        ],
        'slave-mock': [
          {
            processId: 'proc-2',
            status: 'completed',
            startTime: new Date(Date.now() - 8000).toISOString(),
            endTime: new Date(Date.now() - 3000).toISOString(),
            durationMs: 5000,
            command: 'dotnet test -v minimal',
            outputCount: 2,
            metadata: { source: 'processes-view' },
            result: { exitCode: 0 }
          }
        ]
      },
      processOutputByNode: {
        'master-mock': {
          'proc-1': [
            { timestamp: new Date().toISOString(), processId: 'proc-1', outputType: 'standardoutput', content: 'dev server booting\n' },
            { timestamp: new Date().toISOString(), processId: 'proc-1', outputType: 'systemmessage', content: 'watching for changes\n' }
          ]
        },
        'slave-mock': {
          'proc-2': [
            { timestamp: new Date().toISOString(), processId: 'proc-2', outputType: 'standardoutput', content: 'Test run successful\n' },
            { timestamp: new Date().toISOString(), processId: 'proc-2', outputType: 'systemmessage', content: 'process completed\n' }
          ]
        }
      }
    };

    const json = (payload, status = 200) => new Response(JSON.stringify(payload), {
      status,
      headers: { 'content-type': 'application/json' }
    });

    const textResponse = (payload, status = 200, headers = {}) => new Response(payload, {
      status,
      headers
    });

    const normalizePath = (value) => {
      const text = String(value || '').trim() || '/home/yueyuan';
      const compact = text.replace(/\/+/g, '/');
      if (compact.length > 1 && compact.endsWith('/')) {
        return compact.slice(0, -1);
      }
      return compact;
    };

    const getParentPath = (path) => {
      const normalized = normalizePath(path);
      if (normalized === '/' || normalized === '/home' || normalized === '/home/yueyuan') {
        return normalized === '/home/yueyuan' ? '/home' : '';
      }
      const index = normalized.lastIndexOf('/');
      if (index <= 0) {
        return '/';
      }
      return normalized.slice(0, index);
    };

    const getName = (path) => {
      const normalized = normalizePath(path);
      if (normalized === '/') {
        return '/';
      }
      return normalized.split('/').pop() || normalized;
    };

    const listDirectory = (path, showHidden) => {
      const dirPath = normalizePath(path);
      const names = [];
      for (const [candidatePath, item] of Object.entries(state.files)) {
        if (!item || candidatePath === dirPath) {
          continue;
        }
        if (getParentPath(candidatePath) !== dirPath) {
          continue;
        }
        const name = getName(candidatePath);
        if (!showHidden && name.startsWith('.')) {
          continue;
        }
        names.push({
          path: candidatePath,
          name,
          kind: item.kind,
          size: item.kind === 'file' ? String(item.content || '').length : 0
        });
      }
      return names.sort((left, right) => {
        if (left.kind !== right.kind) {
          return left.kind === 'dir' ? -1 : 1;
        }
        return left.name.localeCompare(right.name, 'en');
      });
    };

    globalThis.__PW_MOCK_STATE__ = state;

    const listNodeProcesses = (nodeId) => Array.isArray(state.processesByNode[nodeId]) ? state.processesByNode[nodeId] : [];
    const ensureNodeProcessOutput = (nodeId) => {
      if (!state.processOutputByNode[nodeId]) {
        state.processOutputByNode[nodeId] = {};
      }
      return state.processOutputByNode[nodeId];
    };
    const findProcess = (nodeId, processId) => listNodeProcesses(nodeId).find((item) => item.processId === processId) || null;

    const nativeFetch = globalThis.fetch.bind(globalThis);
    globalThis.fetch = async (input, init = {}) => {
      const reqUrl = typeof input === 'string' ? input : input?.url || '';
      const url = new URL(reqUrl, globalThis.location.origin);
      const method = String(init.method || 'GET').toUpperCase();
      const pathname = url.pathname;
      const normalizedPathname = pathname.replace(/^\/api\/nodes\/[^/]+/, '/api');

      if (!pathname.startsWith('/api/')) {
        return nativeFetch(input, init);
      }

      if (pathname === '/api/instances' && method === 'GET') {
        return json({ items: state.instances });
      }

      if (pathname === '/api/nodes' && method === 'GET') {
        return json({
          items: state.nodes.map((node) => ({
            ...node,
            instance_count: state.instances.filter((item) => item.node_id === node.node_id).length,
            last_seen_at: new Date().toISOString()
          }))
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
        state.screenByInstance[id] = {
          cols,
          rows,
          lines: ['mock ready'],
          seq: 1,
          inputBuffer: '',
          rawChunks: [{ seq: 1, data: 'mock ready\r\n' }]
        };
        return json({ instance_id: id, node_id: 'master-mock', hub_url: `${globalThis.location.origin}/hubs/terminal` });
      }

      const nodeInstanceMatch = pathname.match(/^\/api\/nodes\/([^/]+)\/instances$/);
      if (nodeInstanceMatch && method === 'POST') {
        const nodeId = decodeURIComponent(nodeInstanceMatch[1] || '');
        const targetNode = state.nodes.find((item) => item.node_id === nodeId);
        if (!targetNode) {
          return textResponse('node not found', 404);
        }
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
          node_id: nodeId,
          node_name: targetNode.node_name,
          node_role: targetNode.node_role,
          node_online: targetNode.node_online
        });
        state.screenByInstance[id] = {
          cols,
          rows,
          lines: [`${nodeId} ready`],
          seq: 1,
          inputBuffer: '',
          rawChunks: [{ seq: 1, data: `${nodeId} ready\r\n` }]
        };
        return json({ instance_id: id, node_id: nodeId, hub_url: `${globalThis.location.origin}/hubs/terminal` });
      }

      if (pathname.startsWith('/api/instances/') && method === 'DELETE') {
        const id = decodeURIComponent(pathname.split('/').pop() || '');
        state.instances = state.instances.filter((x) => x.id !== id);
        delete state.screenByInstance[id];
        return json({ ok: true });
      }

      const nodeInstanceDeleteMatch = pathname.match(/^\/api\/nodes\/([^/]+)\/instances\/([^/]+)$/);
      if (nodeInstanceDeleteMatch && method === 'DELETE') {
        const instanceId = decodeURIComponent(nodeInstanceDeleteMatch[2] || '');
        state.instances = state.instances.filter((x) => x.id !== instanceId);
        delete state.screenByInstance[instanceId];
        return json({ ok: true, node_id: decodeURIComponent(nodeInstanceDeleteMatch[1] || ''), instance_id: instanceId });
      }

      const nodeProcessesMatch = pathname.match(/^\/api\/nodes\/([^/]+)\/processes(?:\/([^/]+)(?:\/(output|wait|stop))?)?$/);
      if (nodeProcessesMatch) {
        const nodeId = decodeURIComponent(nodeProcessesMatch[1] || '');
        const processId = nodeProcessesMatch[2] ? decodeURIComponent(nodeProcessesMatch[2]) : '';
        const action = nodeProcessesMatch[3] || '';
        if (!state.nodes.some((item) => item.node_id === nodeId)) {
          return textResponse('node not found', 404);
        }

        if (!processId && method === 'GET') {
          return json({ items: listNodeProcesses(nodeId) });
        }

        if (!processId && method === 'POST') {
          const body = init.body ? JSON.parse(String(init.body)) : {};
          const nextId = `proc-${state.nextProcessId++}`;
          const command = String([body.file, ...(Array.isArray(body.args) ? body.args : [])].filter(Boolean).join(' ') || 'bash');
          const item = {
            processId: nextId,
            status: 'running',
            startTime: new Date().toISOString(),
            endTime: null,
            durationMs: 0,
            command,
            outputCount: 1,
            metadata: body.metadata || {},
            result: null
          };
          state.processesByNode[nodeId] = [item, ...listNodeProcesses(nodeId)];
          ensureNodeProcessOutput(nodeId)[nextId] = [
            { timestamp: new Date().toISOString(), processId: nextId, outputType: 'systemmessage', content: `started on ${nodeId}\n` }
          ];
          return json({ processId: nextId, status: 'running' });
        }

        const item = findProcess(nodeId, processId);
        if (!item) {
          return textResponse('process not found', 404);
        }

        if (!action && method === 'GET') {
          return json(item);
        }

        if (action === 'output' && method === 'GET') {
          return json({ items: ensureNodeProcessOutput(nodeId)[processId] || [] });
        }

        if (action === 'wait' && method === 'POST') {
          return json({ processId, status: item.status, completed: item.status !== 'running', result: item.result });
        }

        if (action === 'stop' && method === 'POST') {
          item.status = 'completed';
          item.endTime = new Date().toISOString();
          item.durationMs = Math.max(1000, Number(item.durationMs || 0));
          item.result = { exitCode: 0 };
          const outputs = ensureNodeProcessOutput(nodeId)[processId] || [];
          outputs.push({ timestamp: new Date().toISOString(), processId, outputType: 'systemmessage', content: 'stopped by user\n' });
          ensureNodeProcessOutput(nodeId)[processId] = outputs;
          return json({ ok: true, processId, status: 'completed' });
        }

        if (!action && method === 'DELETE') {
          state.processesByNode[nodeId] = listNodeProcesses(nodeId).filter((entry) => entry.processId !== processId);
          delete ensureNodeProcessOutput(nodeId)[processId];
          return json({ ok: true, processId });
        }
      }

      if (pathname.startsWith('/api/nodes/') && pathname.endsWith('/files/upload') && method === 'POST') {
        return json({ node_id: 'master-mock', instance_id: 'mock-1', upload: { path: '/tmp/mock-upload.png', size: 11 } });
      }

      if (normalizedPathname === '/api/files/list' && method === 'GET') {
        const path = normalizePath(url.searchParams.get('path'));
        const target = state.files[path];
        if (!target || target.kind !== 'dir') {
          return textResponse('path not found', 404);
        }
        return json({
          base: '/home/yueyuan',
          path,
          parent: getParentPath(path),
          items: listDirectory(path, url.searchParams.get('show_hidden') === '1')
        });
      }

      if (normalizedPathname === '/api/files/read' && method === 'GET') {
        const path = normalizePath(url.searchParams.get('path'));
        const target = state.files[path];
        if (!target || target.kind !== 'file') {
          return textResponse('file not found', 404);
        }
        const content = String(target.content || '');
        return json({
          path,
          content,
          size: content.length,
          lines_shown: content.split('\n').length,
          truncated: false,
          truncate_reason: null
        });
      }

      if (normalizedPathname === '/api/files/write' && method === 'POST') {
        const body = init.body ? JSON.parse(String(init.body)) : {};
        const path = normalizePath(body.path);
        if (!state.files[path] || state.files[path].kind !== 'file') {
          return textResponse('file not found', 404);
        }
        const content = String(body.content ?? '');
        state.files[path] = {
          kind: 'file',
          content
        };
        return json({ ok: true, path, size: content.length });
      }

      if (normalizedPathname === '/api/files/mkdir' && method === 'POST') {
        const body = init.body ? JSON.parse(String(init.body)) : {};
        const parentPath = normalizePath(body.path);
        const name = String(body.name || '').trim();
        if (!name) {
          return textResponse('name is required', 400);
        }
        const nextPath = normalizePath(`${parentPath}/${name}`);
        state.files[nextPath] = { kind: 'dir' };
        return json({ item: { path: nextPath, name, kind: 'dir', size: 0 } });
      }

      if (normalizedPathname === '/api/files/rename' && method === 'POST') {
        const body = init.body ? JSON.parse(String(init.body)) : {};
        const path = normalizePath(body.path);
        const newName = String(body.new_name || '').trim();
        const target = state.files[path];
        if (!target) {
          return textResponse('path not found', 404);
        }
        if (!newName) {
          return textResponse('new name is required', 400);
        }
        const nextPath = normalizePath(`${getParentPath(path)}/${newName}`);
        const nextFiles = {};
        for (const [candidatePath, item] of Object.entries(state.files)) {
          if (candidatePath === path || candidatePath.startsWith(`${path}/`)) {
            const suffix = candidatePath.slice(path.length);
            nextFiles[`${nextPath}${suffix}`] = item;
          } else {
            nextFiles[candidatePath] = item;
          }
        }
        state.files = nextFiles;
        return json({ item: { path: nextPath, name: newName, kind: target.kind, size: String(target.content || '').length } });
      }

      if (normalizedPathname === '/api/files/remove' && method === 'DELETE') {
        const path = normalizePath(url.searchParams.get('path'));
        const nextFiles = {};
        for (const [candidatePath, item] of Object.entries(state.files)) {
          if (candidatePath === path || candidatePath.startsWith(`${path}/`)) {
            continue;
          }
          nextFiles[candidatePath] = item;
        }
        state.files = nextFiles;
        return json({ ok: true });
      }

      if (normalizedPathname === '/api/files/upload' && method === 'POST') {
        const targetPath = normalizePath(init.body?.get?.('path') || url.searchParams.get('path') || '/home/yueyuan/demo');
        const fileName = 'upload.txt';
        const filePath = normalizePath(`${targetPath}/${fileName}`);
        state.files[filePath] = {
          kind: 'file',
          content: 'uploaded mock\n'
        };
        return json({ upload: { path: filePath, size: 14 } });
      }

      if (normalizedPathname === '/api/files/download' && method === 'GET') {
        const path = normalizePath(url.searchParams.get('path'));
        const name = getName(path);
        return textResponse('mock download', 200, {
          'content-type': 'application/octet-stream',
          'content-disposition': `attachment; filename="${name || 'download.bin'}"`
        });
      }

      return new Response(`unhandled mock api: ${pathname} ${method}`, { status: 500 });
    };

    function getScreen(instanceId) {
      const key = String(instanceId || 'mock-1');
      if (!state.screenByInstance[key]) {
        state.screenByInstance[key] = {
          cols: 80,
          rows: 25,
          lines: ['mock ready'],
          seq: 1,
          inputBuffer: '',
          rawChunks: [{ seq: 1, data: 'mock ready\r\n' }]
        };
      }
      return state.screenByInstance[key];
    }

    function pushRaw(screen, data) {
      const text = String(data || '');
      if (!text) {
        return null;
      }
      screen.seq += 1;
      const seq = screen.seq;
      if (!Array.isArray(screen.rawChunks)) {
        screen.rawChunks = [];
      }
      screen.rawChunks.push({ seq, data: text });
      if (screen.rawChunks.length > 400) {
        screen.rawChunks = screen.rawChunks.slice(screen.rawChunks.length - 400);
      }
      return seq;
    }

    function toSnapshot(instanceId) {
      const screen = getScreen(instanceId);
      screen.seq += 1;
      const visibleRows = Math.max(1, Number(screen.rows) || 25);
      const visibleLines = screen.lines.slice(Math.max(0, screen.lines.length - visibleRows));
      const historyLines = screen.lines.slice(0, Math.max(0, screen.lines.length - visibleRows));
      const newestCursor = `h-${historyLines.length + 1}`;
      return {
        v: 1,
        type: 'term.snapshot',
        instance_id: instanceId,
        seq: screen.seq,
        ts: Date.now(),
        size: { cols: screen.cols, rows: screen.rows },
        cursor: { x: 0, y: Math.max(0, visibleLines.length - 1), visible: true },
        styles: { '0': {} },
        rows: visibleLines.map((line, y) => ({ y, segs: [[line, 0]] })),
        history: { available: historyLines.length, newest_cursor: newestCursor }
      };
    }

    function toHistoryChunk(instanceId, reqId, before, limit) {
      const screen = getScreen(instanceId);
      const visibleRows = Math.max(1, Number(screen.rows) || 25);
      const historyLines = screen.lines.slice(0, Math.max(0, screen.lines.length - visibleRows));
      const beforeCursor = Number(String(before || 'h-1').replace(/^h-/, ''));
      const effectiveBefore = Number.isFinite(beforeCursor) && beforeCursor > 0 ? beforeCursor : historyLines.length + 1;
      const candidates = historyLines
        .map((text, index) => ({ cursor: `h-${index + 1}`, text }))
        .filter((item) => Number(item.cursor.slice(2)) < effectiveBefore);
      const safeLimit = Math.max(1, Math.min(200, Number(limit) || 50));
      const lines = candidates.slice(Math.max(0, candidates.length - safeLimit));
      return {
        v: 1,
        type: 'term.history.chunk',
        instance_id: instanceId,
        req_id: reqId,
        lines: lines.map((line) => ({ segs: [[line.text, 0]] })),
        next_before: lines.length > 0 ? lines[0].cursor : `h-${effectiveBefore}`,
        exhausted: candidates.length <= lines.length
      };
    }

    function appendInput(instanceId, data) {
      const screen = getScreen(instanceId);
      const body = String(data || '');
      let output = '';
      for (const ch of body) {
        if (ch === '\r' || ch === '\n') {
          if (screen.inputBuffer.length > 0) {
            const line = `echo:${screen.inputBuffer}`;
            screen.lines.push(line);
            output += `${line}\r\n`;
            screen.inputBuffer = '';
          }
        } else {
          screen.inputBuffer += ch;
        }
      }
      if (screen.lines.length > screen.rows) {
        screen.lines = screen.lines.slice(screen.lines.length - screen.rows);
      }
      const seq = pushRaw(screen, output);
      if (!seq) {
        return null;
      }
      return {
        v: 1,
        type: 'term.raw',
        instance_id: instanceId,
        replay: false,
        seq,
        ts: Date.now(),
        data: output
      };
    }

    function toRawReplay(instanceId, reqId, sinceSeq) {
      const screen = getScreen(instanceId);
      const requestedSince = Math.max(0, Number(sinceSeq || 0));
      const chunks = Array.isArray(screen.rawChunks) ? screen.rawChunks : [];
      const oldestSeq = chunks.length > 0 ? Number(chunks[0].seq || 0) : 0;
      let effectiveSince = requestedSince;
      let truncated = false;
      if (requestedSince > 0 && oldestSeq > 0 && requestedSince < oldestSeq - 1) {
        truncated = true;
        effectiveSince = oldestSeq - 1;
      }
      const selected = chunks.filter((chunk) => Number(chunk.seq || 0) > effectiveSince);
      const data = selected.map((chunk) => String(chunk.data || '')).join('');
      const fromSeq = selected.length > 0 ? Number(selected[0].seq || 0) : Math.max(0, effectiveSince + 1);
      const toSeq = Math.max(0, Number(screen.seq || 0));
      return {
        v: 1,
        type: 'term.raw',
        instance_id: instanceId,
        replay: true,
        req_id: reqId,
        since_seq: requestedSince,
        from_seq: fromSeq,
        to_seq: toSeq,
        seq: toSeq,
        reset: requestedSince <= 0 || truncated,
        truncated,
        oldest_seq: oldestSeq,
        ts: Date.now(),
        data
      };
    }

    function emitServerRaw(instanceId, data) {
      const id = String(instanceId || 'mock-1');
      const screen = getScreen(id);
      const seq = pushRaw(screen, data);
      if (!seq) {
        return null;
      }
      const raw = {
        v: 1,
        type: 'term.raw',
        instance_id: id,
        replay: false,
        seq,
        ts: Date.now(),
        data: String(data || '')
      };
      state.hubConnection?.emit('TerminalEvent', raw);
      return raw;
    }

    state.emitServerRaw = emitServerRaw;

    class MockHubConnection {
      constructor(url = '') {
        this.handlers = new Map();
        this.state = 'Disconnected';
        this.instanceId = 'mock-1';
        this.reconnectingHandler = null;
        this.reconnectedHandler = null;
        this.closeHandler = null;
        this.url = String(url || '');
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
          type: 'term.sync.required',
          instance_id: this.instanceId,
          reason: 'seq_gap',
          node_id: 'master-mock',
          node_name: 'Master Mock'
        });
      }

      async invoke(method, payload = {}) {
        state.invokes.push({ method, payload });

        if (method === 'JoinInstance') {
          this.instanceId = String(payload.instanceId || 'mock-1');
          if (!state.joinedInstanceIds.includes(this.instanceId)) {
            state.joinedInstanceIds.push(this.instanceId);
          }
          this.emit('TerminalEvent', toSnapshot(this.instanceId));
          return;
        }

        if (method === 'LeaveInstance') {
          const id = String(payload.instanceId || '').trim();
          if (id) {
            state.joinedInstanceIds = state.joinedInstanceIds.filter((item) => item !== id);
          } else {
            state.joinedInstanceIds = [];
          }
          return;
        }

        if (method === 'RequestSync') {
          const id = String(payload.instanceId || this.instanceId);
          const syncType = String(payload.type || 'raw').toLowerCase();
          if (syncType === 'raw') {
            const reqId = String(payload.reqId || `raw-sync-${Date.now()}`);
            const replay = toRawReplay(id, reqId, payload.sinceSeq);
            this.emit('TerminalEvent', replay);
            this.emit('TerminalEvent', {
              v: 1,
              type: 'term.sync.complete',
              instance_id: id,
              req_id: reqId,
              to_seq: replay.to_seq,
              ts: Date.now()
            });
            return;
          }
          if (syncType === 'history') {
            const reqId = String(payload.reqId || `history-${Date.now()}`);
            this.emit('TerminalEvent', toHistoryChunk(id, reqId, payload.before, payload.limit));
            return;
          }
          this.emit('TerminalEvent', toSnapshot(id));
          return;
        }

        if (method === 'SendInput') {
          const id = String(payload.instanceId || this.instanceId);
          state.wsInputs.push(String(payload.data || ''));
          const raw = appendInput(id, payload.data);
          if (raw) {
            this.emit('TerminalEvent', raw);
          }
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
            accepted: true,
            size: { cols: screen.cols, rows: screen.rows },
            ts: Date.now()
          });
          this.emit('TerminalEvent', toSnapshot(id));
        }
      }
    }

    class MockHubConnectionBuilder {
      constructor() {
        this.url = '';
      }
      withUrl(url) {
        this.url = url;
        return this;
      }
      withAutomaticReconnect() {
        return this;
      }
      configureLogging() {
        return this;
      }
      build() {
        const conn = new MockHubConnection(this.url);
        state.hubConnection = conn;
        return conn;
      }
    }

    globalThis.__WEBCLI_SIGNALR_BUILDER__ = () => new MockHubConnectionBuilder();
  });
}
