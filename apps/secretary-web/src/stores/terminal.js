import { defineStore } from 'pinia';

const ACTIVE_SESSION_KEY = 'terminal.activeSessionId';
const WRITE_TOKEN_MAP_KEY = 'terminal.sessionWriteTokens';
const WS_BASE = import.meta.env.VITE_TERMINAL_WS_BASE || '';
const WS_TOKEN = import.meta.env.VITE_TERMINAL_WS_TOKEN || '';
const TERMINAL_API_BASE = import.meta.env.VITE_TERMINAL_API_BASE || '/terminal-api';
const RECONNECT_DELAYS_MS = [1000, 2000, 4000, 8000, 15000];
const MAX_RECONNECT_ATTEMPTS = 20;
const MAX_SESSION_BUFFER_CHARS = 8 * 1024 * 1024;
const DEFAULT_HISTORY_LIMIT_BYTES = 256 * 1024;
const PRELOAD_TAIL_LIMIT_BYTES = Number(import.meta.env.VITE_TERMINAL_PRELOAD_TAIL_LIMIT_BYTES || 512 * 1024);
const PRELOAD_MAX_SESSIONS = Number(import.meta.env.VITE_TERMINAL_PRELOAD_MAX_SESSIONS || 5);
const PRELOAD_CONCURRENCY = Number(import.meta.env.VITE_TERMINAL_PRELOAD_CONCURRENCY || 2);

function buildWsUrl(sessionId, options = {}) {
  const replayMode = normalizeReplayMode(options.replayMode || (options.replay ? 'full' : 'none'));
  const sinceSeq = Number.isFinite(Number(options.sinceSeq)) ? Math.trunc(Number(options.sinceSeq)) : null;
  const writeToken = String(options.writeToken || '').trim();
  const defaultBase = typeof window !== 'undefined'
    ? `${window.location.protocol === 'https:' ? 'wss:' : 'ws:'}//${window.location.host}`
    : 'ws://127.0.0.1:7300';
  const url = new URL('/ws/terminal', WS_BASE || defaultBase);
  url.searchParams.set('sessionId', sessionId);
  url.searchParams.set('replayMode', replayMode);
  if (sinceSeq !== null) {
    url.searchParams.set('sinceSeq', String(sinceSeq));
  }
  if (replayMode === 'full') {
    url.searchParams.set('replay', '1');
  }
  if (writeToken) {
    url.searchParams.set('writeToken', writeToken);
  }
  if (WS_TOKEN) {
    url.searchParams.set('token', WS_TOKEN);
  }
  return url.toString();
}

function loadPersistedActiveSession() {
  if (typeof window === 'undefined') {
    return '';
  }
  return String(window.localStorage.getItem(ACTIVE_SESSION_KEY) || '').trim();
}

function loadPersistedWriteTokens() {
  if (typeof window === 'undefined') {
    return {};
  }
  try {
    const raw = window.localStorage.getItem(WRITE_TOKEN_MAP_KEY);
    if (!raw) {
      return {};
    }
    const parsed = JSON.parse(raw);
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
      return {};
    }
    const map = {};
    for (const [key, value] of Object.entries(parsed)) {
      const sessionId = String(key || '').trim();
      const token = String(value || '').trim();
      if (sessionId && token) {
        map[sessionId] = token;
      }
    }
    return map;
  } catch {
    return {};
  }
}

export const useTerminalStore = defineStore('terminal', {
  state: () => ({
    sessions: [],
    activeSessionId: loadPersistedActiveSession(),
    sessionWriteTokens: loadPersistedWriteTokens(),
    loadingSessions: false,
    sessionLoadError: ''
  }),
  getters: {
    activeSession(state) {
      return state.sessions.find((x) => x.sessionId === state.activeSessionId) || null;
    }
  },
  actions: {
    _ensureRuntime() {
      if (!this._runtime) {
        this._runtime = new Map();
      }
      return this._runtime;
    },

    _ensureRuntimeEntry(sessionId) {
      const runtime = this._ensureRuntime();
      let entry = runtime.get(sessionId);
      if (!entry) {
        entry = {
          ws: null,
          listeners: new Set(),
          pingTimer: null,
          reconnectTimer: null,
          manualStop: false,
          reconnectAttempts: 0,
          replayMode: 'none',
          replayHydrated: false,
          outputBuffer: '',
          outputTruncated: false,
          headSeq: 1,
          tailSeq: 0,
          canDeltaReplay: true,
          historyLoading: false,
          historyHasMore: true
        };
        runtime.set(sessionId, entry);
      }
      return entry;
    },

    _emitSyntheticReplay(sessionId, data) {
      const runtime = this._ensureRuntime();
      const entry = runtime.get(sessionId);
      if (!entry) {
        return;
      }
      const message = { type: 'output', sessionId, data: String(data || ''), replay: true };
      for (const listener of entry.listeners) {
        listener(message);
      }
    },

    _upsertSession(sessionPatch) {
      if (!sessionPatch?.sessionId) {
        return null;
      }

      const idx = this.sessions.findIndex((x) => x.sessionId === sessionPatch.sessionId);
      if (idx < 0) {
        const created = {
          sessionId: sessionPatch.sessionId,
          title: sessionPatch.title || String(sessionPatch.sessionId).slice(0, 8),
          mode: sessionPatch.mode || 'execute',
          profileId: sessionPatch.profileId || null,
          cliType: sessionPatch.cliType || 'custom',
          taskId: sessionPatch.taskId || '',
          cwd: sessionPatch.cwd || '',
          shell: sessionPatch.shell || '',
          args: Array.isArray(sessionPatch.args) ? sessionPatch.args : [],
          status: sessionPatch.status || 'running',
          connectionStatus: sessionPatch.connectionStatus || 'idle',
          writable: sessionPatch.writable !== false,
          pid: sessionPatch.pid || null,
          lastActivityAt: sessionPatch.lastActivityAt || new Date().toISOString(),
          exitCode: sessionPatch.exitCode ?? null,
          lastError: sessionPatch.lastError || '',
          outputTruncated: sessionPatch.outputTruncated === true,
          outputBytes: Number(sessionPatch.outputBytes || 0),
          maxOutputBufferBytes: Number(sessionPatch.maxOutputBufferBytes || MAX_SESSION_BUFFER_CHARS)
        };
        this.sessions.push(created);
        return created;
      }

      const merged = {
        ...this.sessions[idx],
        ...sessionPatch,
        sessionId: this.sessions[idx].sessionId
      };
      this.sessions[idx] = merged;
      return merged;
    },

    updateSessionLocal(sessionId, patch = {}) {
      if (!sessionId) {
        return null;
      }
      return this._upsertSession({ sessionId, ...patch });
    },

    _setConnectionStatus(sessionId, status, error = '') {
      const session = this._upsertSession({ sessionId, connectionStatus: status, lastError: error });
      if (!session) {
        return;
      }

      const runtime = this._ensureRuntime();
      const entry = runtime.get(sessionId);
      if (!entry) {
        return;
      }

      if (status === 'connected') {
        entry.reconnectAttempts = 0;
      }
    },

    _appendSessionOutput(entry, data, direction = 'append') {
      if (!entry) {
        return { truncated: false, bytes: 0 };
      }
      const text = String(data || '');
      if (!text) {
        return { truncated: entry.outputTruncated === true, bytes: Number(entry.outputBuffer?.length || 0) };
      }
      const merged = direction === 'prepend' ? `${text}${entry.outputBuffer || ''}` : `${entry.outputBuffer || ''}${text}`;
      const truncated = merged.length > MAX_SESSION_BUFFER_CHARS;
      if (merged.length <= MAX_SESSION_BUFFER_CHARS) {
        entry.outputBuffer = merged;
      } else if (direction === 'prepend') {
        entry.outputBuffer = merged.slice(0, MAX_SESSION_BUFFER_CHARS);
      } else {
        entry.outputBuffer = merged.slice(merged.length - MAX_SESSION_BUFFER_CHARS);
      }
      entry.outputTruncated = entry.outputTruncated === true || truncated;
      return { truncated: entry.outputTruncated === true, bytes: entry.outputBuffer.length };
    },

    _teardownRuntime(sessionId) {
      const runtime = this._ensureRuntime();
      const entry = runtime.get(sessionId);
      if (!entry) {
        return;
      }
      entry.manualStop = true;
      if (entry.pingTimer) {
        clearInterval(entry.pingTimer);
        entry.pingTimer = null;
      }
      if (entry.reconnectTimer) {
        clearTimeout(entry.reconnectTimer);
        entry.reconnectTimer = null;
      }
      if (entry.ws && (entry.ws.readyState === WebSocket.OPEN || entry.ws.readyState === WebSocket.CONNECTING)) {
        entry.ws.close();
      }
      entry.ws = null;
      entry.listeners.clear();
      runtime.delete(sessionId);
    },

    _removeSessionLocal(sessionId) {
      if (!sessionId) {
        return;
      }
      this._teardownRuntime(sessionId);
      this.sessions = this.sessions.filter((x) => x.sessionId !== sessionId);
      if (this.activeSessionId === sessionId) {
        this.activeSessionId = '';
        this._persistActiveSession();
      }
      if (this.sessionWriteTokens?.[sessionId]) {
        delete this.sessionWriteTokens[sessionId];
        this._persistWriteTokens();
      }
    },

    _persistActiveSession() {
      if (typeof window === 'undefined') {
        return;
      }
      if (this.activeSessionId) {
        window.localStorage.setItem(ACTIVE_SESSION_KEY, this.activeSessionId);
      } else {
        window.localStorage.removeItem(ACTIVE_SESSION_KEY);
      }
    },

    _persistWriteTokens() {
      if (typeof window === 'undefined') {
        return;
      }
      window.localStorage.setItem(WRITE_TOKEN_MAP_KEY, JSON.stringify(this.sessionWriteTokens || {}));
    },

    _setWriteToken(sessionId, writeToken) {
      const id = String(sessionId || '').trim();
      const token = String(writeToken || '').trim();
      if (!id) {
        return;
      }

      if (!token) {
        if (this.sessionWriteTokens[id]) {
          delete this.sessionWriteTokens[id];
          this._persistWriteTokens();
        }
        return;
      }

      this.sessionWriteTokens[id] = token;
      this._persistWriteTokens();
    },

    async loadSessions({ includeExited = false, profileId = '', taskId = '' } = {}) {
      this.loadingSessions = true;
      this.sessionLoadError = '';
      try {
        const params = new URLSearchParams();
        params.set('includeExited', includeExited ? '1' : '0');
        if (profileId) {
          params.set('profileId', profileId);
        }
        if (taskId) {
          params.set('taskId', taskId);
        }

        const res = await fetch(`${TERMINAL_API_BASE}/sessions?${params.toString()}`);
        if (!res.ok) {
          throw new Error(await readError(res, `load sessions failed: ${res.status}`));
        }

        const loaded = await res.json();
        const previous = new Map(this.sessions.map((x) => [x.sessionId, x]));
        const normalizedMap = new Map();
        for (const item of loaded) {
          const existing = previous.get(item.sessionId);
          normalizedMap.set(item.sessionId, {
            ...item,
            title: item.title || existing?.title || String(item.sessionId).slice(0, 8),
            connectionStatus: existing?.connectionStatus || (item.status === 'exited' ? 'exited' : 'idle'),
            writable: existing?.writable !== false,
            lastError: existing?.lastError || '',
            outputTruncated: item.outputTruncated === true || existing?.outputTruncated === true,
            outputBytes: Number(item.outputBytes || existing?.outputBytes || 0),
            maxOutputBufferBytes: Number(item.maxOutputBufferBytes || existing?.maxOutputBufferBytes || MAX_SESSION_BUFFER_CHARS)
          });
        }

        const stableOrdered = [];
        for (const oldItem of this.sessions) {
          const next = normalizedMap.get(oldItem.sessionId);
          if (next) {
            stableOrdered.push(next);
            normalizedMap.delete(oldItem.sessionId);
          }
        }

        const newlyDiscovered = Array.from(normalizedMap.values())
          .sort((a, b) => String(a.createdAt || '').localeCompare(String(b.createdAt || '')));

        this.sessions = [...stableOrdered, ...newlyDiscovered];

        if (this.activeSessionId && !this.sessions.some((x) => x.sessionId === this.activeSessionId)) {
          this.activeSessionId = '';
          this._persistActiveSession();
        }

        return this.sessions;
      } catch (err) {
        this.sessionLoadError = String(err?.message || err);
        throw err;
      } finally {
        this.loadingSessions = false;
      }
    },

    async preloadSessionTails({ limitBytes = PRELOAD_TAIL_LIMIT_BYTES, maxSessions = PRELOAD_MAX_SESSIONS, concurrency = PRELOAD_CONCURRENCY } = {}) {
      const targets = this.sessions
        .filter((x) => x.status === 'running')
        .slice(0, Math.max(0, Math.trunc(maxSessions || 0)));
      if (targets.length === 0) {
        return;
      }
      const workerCount = Math.max(1, Math.min(Math.trunc(concurrency || 1), targets.length));
      let cursor = 0;
      const workers = [];
      for (let i = 0; i < workerCount; i += 1) {
        workers.push((async () => {
          while (cursor < targets.length) {
            const index = cursor;
            cursor += 1;
            const session = targets[index];
            try {
              await this.hydrateSessionTail(session.sessionId, { limitBytes });
            } catch {
              // Skip tail preload errors to avoid blocking initial workspace render.
            }
          }
        })());
      }
      await Promise.all(workers);
    },

    async hydrateSessionTail(sessionId, { limitBytes = PRELOAD_TAIL_LIMIT_BYTES } = {}) {
      if (!sessionId) {
        return null;
      }
      const max = clampInt(limitBytes, 1024, MAX_SESSION_BUFFER_CHARS, PRELOAD_TAIL_LIMIT_BYTES);
      const res = await fetch(`${TERMINAL_API_BASE}/sessions/${encodeURIComponent(sessionId)}/snapshot?limitBytes=${max}`);
      if (!res.ok) {
        throw new Error(await readError(res, `snapshot failed: ${res.status}`));
      }
      const snapshot = await res.json();
      const entry = this._ensureRuntimeEntry(sessionId);
      entry.outputBuffer = String(snapshot?.data || '');
      entry.outputTruncated = snapshot?.truncated === true;
      entry.headSeq = Number(snapshot?.headSeq || 1);
      entry.tailSeq = Number(snapshot?.tailSeq || 0);
      entry.historyHasMore = entry.headSeq > 1 || entry.outputTruncated === true;
      this._upsertSession({
        sessionId,
        outputTruncated: entry.outputTruncated,
        outputBytes: Number(snapshot?.totalBytes || snapshot?.bytes || entry.outputBuffer.length),
        maxOutputBufferBytes: Number(snapshot?.maxOutputBufferBytes || MAX_SESSION_BUFFER_CHARS)
      });
      return snapshot;
    },

    openSession(sessionId, title = '') {
      if (!sessionId) {
        return;
      }

      const existing = this.sessions.find((x) => x.sessionId === sessionId);
      this._upsertSession({
        sessionId,
        title: title || existing?.title || String(sessionId).slice(0, 8),
        connectionStatus: existing?.connectionStatus || 'idle'
      });

      this.activeSessionId = sessionId;
      this._persistActiveSession();
      const runtime = this._ensureRuntime();
      for (const [id, entry] of runtime.entries()) {
        if (id === sessionId) {
          continue;
        }
        if (entry.reconnectTimer) {
          clearTimeout(entry.reconnectTimer);
          entry.reconnectTimer = null;
        }
        if (entry.pingTimer) {
          clearInterval(entry.pingTimer);
          entry.pingTimer = null;
        }
        if (entry.ws && (entry.ws.readyState === WebSocket.OPEN || entry.ws.readyState === WebSocket.CONNECTING)) {
          entry.manualStop = true;
          entry.ws.close();
        }
      }
      this.ensureConnection(sessionId);
    },

    subscribe(sessionId, listener) {
      const runtime = this._ensureRuntime();
      const entry = this._ensureRuntimeEntry(sessionId);
      entry.listeners.add(listener);
      runtime.set(sessionId, entry);

      return () => {
        const current = runtime.get(sessionId);
        if (!current) {
          return;
        }
        current.listeners.delete(listener);
      };
    },

    ensureConnection(sessionId) {
      if (!sessionId) {
        return;
      }
      if (this.activeSessionId && this.activeSessionId !== sessionId) {
        return;
      }

      const runtime = this._ensureRuntime();
      const entry = this._ensureRuntimeEntry(sessionId);

      if (entry.ws && (entry.ws.readyState === WebSocket.OPEN || entry.ws.readyState === WebSocket.CONNECTING)) {
        return;
      }

      if (entry.reconnectTimer) {
        clearTimeout(entry.reconnectTimer);
        entry.reconnectTimer = null;
      }

      this._setConnectionStatus(sessionId, entry.reconnectAttempts > 0 ? 'reconnecting' : 'connecting');
      entry.manualStop = false;

      const writeToken = String(this.sessionWriteTokens?.[sessionId] || '').trim();
      const sinceSeq = entry.replayMode === 'none' ? entry.tailSeq : null;
      const ws = new WebSocket(buildWsUrl(sessionId, {
        replayMode: entry.replayMode || 'none',
        sinceSeq,
        writeToken
      }));
      entry.ws = ws;
      runtime.set(sessionId, entry);

      ws.onopen = () => {
        this._setConnectionStatus(sessionId, 'connected');
        if (entry.pingTimer) {
          clearInterval(entry.pingTimer);
        }
        entry.pingTimer = setInterval(() => {
          this.sendRaw(sessionId, { type: 'ping', ts: Date.now() });
        }, 15000);
      };

      ws.onmessage = (evt) => {
        let msg;
        try {
          msg = JSON.parse(evt.data);
        } catch {
          return;
        }

        if (msg.type === 'ready') {
          entry.replayHydrated = false;
          entry.canDeltaReplay = msg.canDeltaReplay !== false;
          entry.headSeq = Number(msg.headSeq || entry.headSeq || 1);
          entry.tailSeq = Number(msg.tailSeq || entry.tailSeq || 0);
          entry.historyHasMore = entry.headSeq > 1 || msg.outputTruncated === true;
          if (entry.replayMode === 'full' || entry.replayMode === 'tail') {
            entry.replayMode = 'none';
          }
          this._upsertSession({
            sessionId,
            taskId: msg.taskId || '',
            profileId: msg.profileId || null,
            title: msg.title || String(sessionId).slice(0, 8),
            cwd: msg.cwd || '',
            shell: msg.shell || '',
            args: Array.isArray(msg.args) ? msg.args : [],
            cliType: msg.cliType || 'custom',
            mode: msg.mode || 'execute',
            writable: msg.writable !== false,
            pid: msg.pid || null,
            status: msg.status || 'running',
            connectionStatus: 'connected',
            outputTruncated: msg.outputTruncated === true,
            outputBytes: Number(msg.outputBytes || 0),
            maxOutputBufferBytes: Number(msg.maxOutputBufferBytes || MAX_SESSION_BUFFER_CHARS)
          });
        }

        if (msg.type === 'output') {
          if (msg.truncatedSince === true && entry.replayMode === 'none') {
            this.reconnectNow(sessionId, { replayMode: 'tail' });
            return;
          }
          if (msg.replay === true && entry.replayHydrated !== true) {
            entry.outputBuffer = '';
            entry.outputTruncated = false;
            entry.replayHydrated = true;
          }
          const outputState = this._appendSessionOutput(entry, msg.data);
          if (Number.isFinite(Number(msg.seqStart))) {
            entry.headSeq = entry.headSeq > 0 ? Math.min(entry.headSeq, Number(msg.seqStart)) : Number(msg.seqStart);
          }
          if (Number.isFinite(Number(msg.seqEnd))) {
            entry.tailSeq = Math.max(entry.tailSeq, Number(msg.seqEnd));
          }
          if (entry.headSeq < 1) {
            entry.headSeq = 1;
          }
          entry.historyHasMore = entry.headSeq > 1 || outputState.truncated === true;
          this._upsertSession({
            sessionId,
            lastActivityAt: new Date().toISOString(),
            outputTruncated: outputState.truncated,
            outputBytes: outputState.bytes
          });
        }

        if (msg.type === 'exit') {
          this._upsertSession({
            sessionId,
            status: 'exited',
            exitCode: msg.exitCode ?? null,
            connectionStatus: 'exited',
            lastActivityAt: new Date().toISOString()
          });
          entry.manualStop = true;
          if (entry.reconnectTimer) {
            clearTimeout(entry.reconnectTimer);
            entry.reconnectTimer = null;
          }
        }

        for (const listener of entry.listeners) {
          listener(msg);
        }
      };

      ws.onerror = () => {
        this._setConnectionStatus(sessionId, 'error', 'websocket error');
      };

      ws.onclose = () => {
        if (entry.ws === ws) {
          entry.ws = null;
        }
        if (entry.pingTimer) {
          clearInterval(entry.pingTimer);
          entry.pingTimer = null;
        }

        const current = this.sessions.find((x) => x.sessionId === sessionId);
        if (!current) {
          return;
        }
        if (this.activeSessionId && this.activeSessionId !== sessionId) {
          return;
        }

        if (entry.manualStop || current.connectionStatus === 'exited' || current.status === 'exited') {
          return;
        }

        if (entry.reconnectAttempts >= MAX_RECONNECT_ATTEMPTS) {
          this._setConnectionStatus(sessionId, 'error', 'reconnect limit reached');
          return;
        }

        entry.reconnectAttempts += 1;
        entry.replayHydrated = false;
        entry.replayMode = 'none';
        this._setConnectionStatus(sessionId, 'reconnecting');

        const delay = RECONNECT_DELAYS_MS[Math.min(entry.reconnectAttempts - 1, RECONNECT_DELAYS_MS.length - 1)];
        entry.reconnectTimer = setTimeout(() => {
          entry.reconnectTimer = null;
          this.ensureConnection(sessionId);
        }, delay);
      };
    },

    reconnectNow(sessionId, { replay = true, replayMode = '' } = {}) {
      if (!sessionId) {
        return;
      }

      const runtime = this._ensureRuntime();
      const entry = this._ensureRuntimeEntry(sessionId);

      entry.manualStop = false;
      entry.reconnectAttempts = 0;
      entry.replayHydrated = false;
      entry.replayMode = normalizeReplayMode(replayMode || (replay === true ? 'full' : 'none'));

      if (entry.reconnectTimer) {
        clearTimeout(entry.reconnectTimer);
        entry.reconnectTimer = null;
      }

      if (entry.ws && (entry.ws.readyState === WebSocket.OPEN || entry.ws.readyState === WebSocket.CONNECTING)) {
        entry.ws.close();
      } else {
        this.ensureConnection(sessionId);
      }
    },

    disconnect(sessionId, manualStop = true) {
      if (!sessionId) {
        return;
      }

      const runtime = this._ensureRuntime();
      const entry = runtime.get(sessionId);
      if (!entry) {
        this._setConnectionStatus(sessionId, 'idle');
        return;
      }

      entry.manualStop = manualStop;
      if (entry.pingTimer) {
        clearInterval(entry.pingTimer);
        entry.pingTimer = null;
      }
      if (entry.reconnectTimer) {
        clearTimeout(entry.reconnectTimer);
        entry.reconnectTimer = null;
      }

      if (entry.ws && (entry.ws.readyState === WebSocket.OPEN || entry.ws.readyState === WebSocket.CONNECTING)) {
        entry.ws.close();
      }
      entry.ws = null;

      if (manualStop) {
        this._setConnectionStatus(sessionId, 'idle');
      }
    },

    sendRaw(sessionId, payload) {
      const runtime = this._ensureRuntime();
      const entry = runtime.get(sessionId);
      if (!entry?.ws || entry.ws.readyState !== WebSocket.OPEN) {
        return false;
      }

      entry.ws.send(JSON.stringify(payload));
      return true;
    },

    sendInput(sessionId, data) {
      const session = this.sessions.find((x) => x.sessionId === sessionId);
      if (session && session.writable === false) {
        this._upsertSession({
          sessionId,
          lastError: 'session is read-only'
        });
        return false;
      }
      return this.sendRaw(sessionId, { type: 'input', data });
    },

    sendResize(sessionId, cols, rows) {
      return this.sendRaw(sessionId, { type: 'resize', cols, rows });
    },

    async createSession(payload = {}) {
      const res = await fetch(`${TERMINAL_API_BASE}/sessions`, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(payload)
      });
      if (!res.ok) {
        throw new Error(await readError(res, `create session failed: ${res.status}`));
      }

      const created = await res.json();
      if (created?.writeToken) {
        this._setWriteToken(created.sessionId, created.writeToken);
      }
      const { writeToken: _writeToken, ...createdWithoutToken } = created || {};
      this._upsertSession({
        ...createdWithoutToken,
        writable: true,
        connectionStatus: created.status === 'exited' ? 'exited' : 'idle'
      });
      return created;
    },

    async terminateSession(sessionId, signal = 'SIGTERM') {
      const res = await fetch(`${TERMINAL_API_BASE}/sessions/${encodeURIComponent(sessionId)}/terminate`, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ signal })
      });
      if (!res.ok) {
        throw new Error(await readError(res, `terminate failed: ${res.status}`));
      }
      return await res.json();
    },

    async loadOlderHistory(sessionId, { limitBytes = DEFAULT_HISTORY_LIMIT_BYTES } = {}) {
      if (!sessionId) {
        return { loaded: false, reason: 'missing-session' };
      }
      const entry = this._ensureRuntimeEntry(sessionId);
      if (entry.historyLoading) {
        return { loaded: false, reason: 'already-loading' };
      }
      if (entry.historyHasMore === false) {
        return { loaded: false, reason: 'no-more' };
      }

      entry.historyLoading = true;
      try {
        const max = clampInt(limitBytes, 1024, MAX_SESSION_BUFFER_CHARS, DEFAULT_HISTORY_LIMIT_BYTES);
        const beforeSeq = Number.isFinite(entry.headSeq) && entry.headSeq > 1 ? entry.headSeq : null;
        const params = new URLSearchParams();
        if (beforeSeq !== null) {
          params.set('beforeSeq', String(beforeSeq));
        }
        params.set('limitBytes', String(max));
        const res = await fetch(`${TERMINAL_API_BASE}/sessions/${encodeURIComponent(sessionId)}/history?${params.toString()}`);
        if (!res.ok) {
          throw new Error(await readError(res, `history failed: ${res.status}`));
        }
        const body = await res.json();
        const chunks = Array.isArray(body?.chunks) ? body.chunks : [];
        if (chunks.length === 0) {
          entry.historyHasMore = body?.hasMore === true;
          return { loaded: false, reason: 'empty' };
        }

        const merged = chunks.map((x) => String(x?.data || '')).join('');
        const firstSeq = Number(chunks[0]?.seqStart || entry.headSeq || 1);
        entry.headSeq = Number.isFinite(firstSeq) ? firstSeq : entry.headSeq;
        entry.historyHasMore = body?.hasMore === true;
        const outputState = this._appendSessionOutput(entry, merged, 'prepend');
        this._upsertSession({
          sessionId,
          outputTruncated: outputState.truncated,
          outputBytes: outputState.bytes
        });
        this._emitSyntheticReplay(sessionId, entry.outputBuffer);
        return { loaded: true, bytes: merged.length, hasMore: entry.historyHasMore };
      } finally {
        entry.historyLoading = false;
      }
    },

    getSessionOutputBuffer(sessionId) {
      if (!sessionId) {
        return '';
      }
      const entry = this._ensureRuntimeEntry(sessionId);
      return String(entry.outputBuffer || '');
    },

    isSessionHistoryLoading(sessionId) {
      if (!sessionId) {
        return false;
      }
      const entry = this._ensureRuntimeEntry(sessionId);
      return entry.historyLoading === true;
    },

    async removeSession(sessionId) {
      const res = await fetch(`${TERMINAL_API_BASE}/sessions/${encodeURIComponent(sessionId)}`, {
        method: 'DELETE'
      });
      if (!res.ok) {
        throw new Error(await readError(res, `remove session failed: ${res.status}`));
      }
      this._removeSessionLocal(sessionId);
      return await res.json();
    },

    async pruneExitedSessions() {
      const res = await fetch(`${TERMINAL_API_BASE}/sessions/prune-exited`, {
        method: 'POST'
      });
      if (!res.ok) {
        throw new Error(await readError(res, `prune exited sessions failed: ${res.status}`));
      }
      const body = await res.json();
      const exitedIds = this.sessions
        .filter((x) => x.status === 'exited' || x.connectionStatus === 'exited')
        .map((x) => x.sessionId);
      for (const sessionId of exitedIds) {
        this._removeSessionLocal(sessionId);
      }
      return body;
    }
  }
});

async function readError(res, fallback) {
  try {
    const text = await res.text();
    return text || fallback;
  } catch {
    return fallback;
  }
}

function clampInt(value, min, max, fallback) {
  const n = Number(value);
  if (!Number.isFinite(n)) {
    return fallback;
  }
  if (n < min) {
    return min;
  }
  if (n > max) {
    return max;
  }
  return Math.trunc(n);
}

function normalizeReplayMode(value) {
  const mode = String(value || '').trim().toLowerCase();
  if (mode === 'none' || mode === 'tail' || mode === 'full') {
    return mode;
  }
  return 'none';
}
