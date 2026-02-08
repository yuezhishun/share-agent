import { defineStore } from 'pinia';

const ACTIVE_SESSION_KEY = 'terminal.activeSessionId';
const WRITE_TOKEN_MAP_KEY = 'terminal.sessionWriteTokens';
const WS_BASE = import.meta.env.VITE_TERMINAL_WS_BASE || '';
const WS_TOKEN = import.meta.env.VITE_TERMINAL_WS_TOKEN || '';
const TERMINAL_API_BASE = import.meta.env.VITE_TERMINAL_API_BASE || '/terminal-api';
const RECONNECT_DELAYS_MS = [1000, 2000, 4000, 8000, 15000];
const MAX_RECONNECT_ATTEMPTS = 20;
const MAX_SESSION_BUFFER_CHARS = 8 * 1024 * 1024;

function buildWsUrl(sessionId, replay = false, writeToken = '') {
  const defaultBase = typeof window !== 'undefined'
    ? `${window.location.protocol === 'https:' ? 'wss:' : 'ws:'}//${window.location.host}`
    : 'ws://127.0.0.1:7300';
  const url = new URL('/ws/terminal', WS_BASE || defaultBase);
  url.searchParams.set('sessionId', sessionId);
  url.searchParams.set('replay', replay ? '1' : '0');
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

    _appendSessionOutput(entry, data) {
      if (!entry) {
        return { truncated: false, bytes: 0 };
      }
      const text = String(data || '');
      if (!text) {
        return { truncated: entry.outputTruncated === true, bytes: Number(entry.outputBuffer?.length || 0) };
      }
      const merged = `${entry.outputBuffer || ''}${text}`;
      const truncated = merged.length > MAX_SESSION_BUFFER_CHARS;
      entry.outputBuffer = merged.length <= MAX_SESSION_BUFFER_CHARS
        ? merged
        : merged.slice(merged.length - MAX_SESSION_BUFFER_CHARS);
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
      this.ensureConnection(sessionId);
    },

    subscribe(sessionId, listener) {
      const runtime = this._ensureRuntime();
      const entry = runtime.get(sessionId) || {
        ws: null,
        listeners: new Set(),
        pingTimer: null,
        reconnectTimer: null,
        manualStop: false,
        reconnectAttempts: 0,
        requestReplay: true,
        replayHydrated: false,
        outputBuffer: '',
        outputTruncated: false
      };
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
          requestReplay: true,
          replayHydrated: false,
          outputBuffer: '',
          outputTruncated: false
        };
        runtime.set(sessionId, entry);
      }

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
      const ws = new WebSocket(buildWsUrl(sessionId, entry.requestReplay === true, writeToken));
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
          entry.requestReplay = false;
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
          if (msg.replay === true && entry.replayHydrated !== true) {
            entry.outputBuffer = '';
            entry.outputTruncated = false;
            entry.replayHydrated = true;
          }
          const outputState = this._appendSessionOutput(entry, msg.data);
          this._upsertSession({
            sessionId,
            lastActivityAt: new Date().toISOString(),
            outputTruncated: outputState.truncated,
            outputBytes: outputState.bytes
          });
        }

        if (msg.type === 'exit') {
          entry.requestReplay = false;
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
        if (entry.pingTimer) {
          clearInterval(entry.pingTimer);
          entry.pingTimer = null;
        }

        const current = this.sessions.find((x) => x.sessionId === sessionId);
        if (!current) {
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
        entry.requestReplay = true;
        entry.replayHydrated = false;
        this._setConnectionStatus(sessionId, 'reconnecting');

        const delay = RECONNECT_DELAYS_MS[Math.min(entry.reconnectAttempts - 1, RECONNECT_DELAYS_MS.length - 1)];
        entry.reconnectTimer = setTimeout(() => {
          entry.reconnectTimer = null;
          this.ensureConnection(sessionId);
        }, delay);
      };
    },

    reconnectNow(sessionId, { replay = true } = {}) {
      if (!sessionId) {
        return;
      }

      const runtime = this._ensureRuntime();
      const entry = runtime.get(sessionId);
      if (!entry) {
        const created = {
          ws: null,
          listeners: new Set(),
          pingTimer: null,
          reconnectTimer: null,
          manualStop: false,
          reconnectAttempts: 0,
          requestReplay: replay === true,
          replayHydrated: false,
          outputBuffer: '',
          outputTruncated: false
        };
        runtime.set(sessionId, created);
        this.ensureConnection(sessionId);
        return;
      }

      entry.manualStop = false;
      entry.reconnectAttempts = 0;
      entry.requestReplay = replay === true;
      entry.replayHydrated = false;

      if (entry.reconnectTimer) {
        clearTimeout(entry.reconnectTimer);
        entry.reconnectTimer = null;
      }

      if (entry.ws && entry.ws.readyState === WebSocket.OPEN) {
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

    getSessionOutputBuffer(sessionId) {
      if (!sessionId) {
        return '';
      }
      const runtime = this._ensureRuntime();
      return String(runtime.get(sessionId)?.outputBuffer || '');
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
