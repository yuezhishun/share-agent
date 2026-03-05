import { defineStore } from 'pinia';
import * as signalR from '@microsoft/signalr';

function resolveHttpBase() {
  const explicit = String(import.meta.env.VITE_WEBPTY_BASE || '/web-pty').trim();
  return explicit;
}

function buildApiPath(pathname) {
  const base = resolveHttpBase();
  if (!base) {
    return pathname;
  }
  return `${base}${pathname}`;
}

function buildHubUrl() {
  const explicit = String(import.meta.env.VITE_WEBPTY_HUB_URL || '').trim();
  if (explicit) {
    return explicit;
  }

  const hubPath = String(import.meta.env.VITE_WEBPTY_HUB_PATH || '/hubs/terminal').trim() || '/hubs/terminal';
  const normalized = hubPath.startsWith('/') ? hubPath : `/${hubPath}`;
  if (typeof window === 'undefined') {
    return normalized;
  }

  return `${window.location.origin}${normalized}`;
}

function createHubConnectionBuilder() {
  if (typeof window !== 'undefined' && typeof window.__WEBCLI_SIGNALR_BUILDER__ === 'function') {
    return window.__WEBCLI_SIGNALR_BUILDER__();
  }

  return new signalR.HubConnectionBuilder();
}

function parseSeq(value, fallback = 0) {
  const seq = Number(value);
  if (!Number.isFinite(seq)) {
    return Math.max(0, Number(fallback) || 0);
  }
  return Math.max(0, Math.floor(seq));
}

const MAX_PLAIN_OUTPUT_CHARS = 12000;
const MAX_ANSI_REPLAY_BYTES_PER_INSTANCE = 4 * 1024 * 1024;
const AUTO_RESPONSE_ROUTE_TTL_MS = 1800;
const textEncoder = typeof TextEncoder === 'function' ? new TextEncoder() : null;
const TERMINAL_QUERY_PROBE_PATTERN = /(?:\u001b\[6n)|(?:\u001b\[(?:\?|>)?[0-9;]*c)|(?:\u001b\](?:10|11|12|13|14|15|16|17|18|19|4;\d+);\?)/;
const TERMINAL_AUTO_RESPONSE_PATTERN = /(?:\u001b\[\d{1,4};\d{1,4}R)|(?:\u001b\[(?:\?|>)[0-9;]*c)|(?:\u001b\][0-9]+;[^\u0007\u001b]*(?:\u0007|\u001b\\)?)/;

function utf8ByteLength(input) {
  const value = String(input || '');
  if (!value) {
    return 0;
  }
  if (textEncoder) {
    return textEncoder.encode(value).length;
  }
  return value.length;
}

export function containsTerminalQueryProbe(input) {
  const value = String(input || '');
  if (!value) {
    return false;
  }
  return TERMINAL_QUERY_PROBE_PATTERN.test(value);
}

export function looksLikeTerminalAutoResponse(input) {
  const value = String(input || '');
  if (!value) {
    return false;
  }
  return TERMINAL_AUTO_RESPONSE_PATTERN.test(value);
}

export const useWebCliTerminalStore = defineStore('webcliTerminal', {
  state: () => ({
    instances: [],
    nodes: [],
    projects: [],
    selectedInstanceId: '',
    selectedProjectPath: '',
    status: 'Ready',
    wsConnected: false,
    isReconnecting: false,
    connection: null,
    joinedInstanceId: '',
    joinedInstanceIds: [],
    listeners: [],
    resizeTimer: null,
    pendingResize: null,
    cachedScreens: {},
    ansiReplayByInstance: {},
    streamStates: {},
    routeResyncTimers: {},
    routeResyncInFlight: {},
    resizeAckByInstance: {},
    plainOutput: '',
    plainOutputByInstance: {},
    terminalResponseRoute: {
      instanceId: '',
      expiresAt: 0
    },
    uiSession: {
      rightPanelCollapsed: false,
      activeRightTab: 'files'
    }
  }),

  getters: {
    selectedInstance(state) {
      return state.instances.find((x) => x.id === state.selectedInstanceId) || null;
    },
    selectedNodeLabel() {
      const instance = this.selectedInstance;
      if (!instance) {
        return '';
      }
      const nodeName = instance.node_name || instance.node_id || 'unknown';
      return `${nodeName}/${instance.id}`;
    }
  },

  actions: {
    setStatus(value) {
      this.status = String(value || 'Ready');
    },

    setUiSession(patch) {
      if (!patch || typeof patch !== 'object') {
        return;
      }
      this.uiSession = {
        ...this.uiSession,
        ...patch
      };
    },

    subscribe(listener) {
      if (typeof listener !== 'function') {
        return () => {};
      }
      this.listeners.push(listener);
      return () => {
        this.listeners = this.listeners.filter((x) => x !== listener);
      };
    },

    emitMessage(message) {
      this.rememberScreenFrame(message);
      this.rememberPlainOutput(message);
      this.rememberAnsiReplay(message);
      const shouldRender = this.isMessageForSelected(message);
      if (!shouldRender) {
        return;
      }
      this.rememberTerminalResponseRoute(message);
      for (const listener of this.listeners) {
        listener(message);
      }
    },

    rememberTerminalResponseRoute(message) {
      if (!message || typeof message !== 'object' || message.type !== 'term.raw') {
        return;
      }
      if (message.local === true) {
        return;
      }

      const instanceId = String(message.instance_id || '').trim();
      if (!instanceId) {
        return;
      }
      const data = String(message.data || '');
      if (!containsTerminalQueryProbe(data)) {
        return;
      }

      this.terminalResponseRoute = {
        instanceId,
        expiresAt: Date.now() + AUTO_RESPONSE_ROUTE_TTL_MS
      };
    },

    resolveInputTargetInstanceId(data) {
      const selectedId = String(this.selectedInstanceId || '').trim();
      if (!looksLikeTerminalAutoResponse(data)) {
        return selectedId;
      }

      const routeInstanceId = String(this.terminalResponseRoute?.instanceId || '').trim();
      const routeExpiresAt = Number(this.terminalResponseRoute?.expiresAt || 0);
      if (!routeInstanceId || !Number.isFinite(routeExpiresAt) || routeExpiresAt < Date.now()) {
        this.terminalResponseRoute = { instanceId: '', expiresAt: 0 };
        return selectedId;
      }

      return routeInstanceId;
    },

    syncPlainOutputForSelected() {
      const id = String(this.selectedInstanceId || '').trim();
      if (!id) {
        this.plainOutput = '';
        return;
      }
      this.plainOutput = String(this.plainOutputByInstance[id] || '');
    },

    rememberPlainOutput(message, options = {}) {
      if (!message || typeof message !== 'object') {
        return;
      }
      const cacheOnly = options?.cacheOnly === true;
      const instanceId = String(message.instance_id || (cacheOnly ? '' : this.selectedInstanceId) || '').trim();
      if (!instanceId) {
        return;
      }

      if (message.type === 'term.raw') {
        const chunk = String(message.data || '');
        const previous = String(this.plainOutputByInstance[instanceId] || '');
        let next = previous;
        if (message.replay === true && message.reset === true) {
          next = chunk;
        } else if (chunk) {
          next = `${previous}${chunk}`;
        } else {
          return;
        }
        const normalized = next.slice(-MAX_PLAIN_OUTPUT_CHARS);
        this.plainOutputByInstance[instanceId] = normalized;
        if (!cacheOnly && instanceId === String(this.selectedInstanceId || '').trim()) {
          this.plainOutput = normalized;
        }
        return;
      }

      if (message.type === 'term.exit') {
        const previous = String(this.plainOutputByInstance[instanceId] || '');
        const next = `${previous}\n[exit] code=${String(message.code ?? '')}`.slice(-MAX_PLAIN_OUTPUT_CHARS);
        this.plainOutputByInstance[instanceId] = next;
        if (!cacheOnly && instanceId === String(this.selectedInstanceId || '').trim()) {
          this.plainOutput = next;
        }
      }
    },

    rememberAnsiReplay(message) {
      if (!message || typeof message !== 'object' || message.type !== 'term.raw') {
        return;
      }
      if (message.local === true) {
        return;
      }
      const instanceId = String(message.instance_id || '').trim();
      if (!instanceId) {
        return;
      }

      const chunk = String(message.data || '');
      const reset = message.replay === true && message.reset === true;
      if (!chunk && !reset) {
        return;
      }

      if (!this.ansiReplayByInstance[instanceId]) {
        this.ansiReplayByInstance[instanceId] = { chunks: [], bytes: 0, hasServerBaseline: false };
      }
      const entry = this.ansiReplayByInstance[instanceId];
      if (!Object.prototype.hasOwnProperty.call(entry, 'hasServerBaseline')) {
        entry.hasServerBaseline = false;
      }

      if (reset) {
        entry.chunks = [];
        entry.bytes = 0;
      }
      if (message.replay === true && reset && parseSeq(message.since_seq, 0) <= 0) {
        entry.hasServerBaseline = true;
      }

      if (chunk) {
        entry.chunks.push(chunk);
        entry.bytes += utf8ByteLength(chunk);
      }

      while (entry.bytes > MAX_ANSI_REPLAY_BYTES_PER_INSTANCE && entry.chunks.length > 0) {
        const removed = entry.chunks.shift();
        entry.bytes = Math.max(0, entry.bytes - utf8ByteLength(removed));
      }
    },

    getAnsiReplayData(instanceId) {
      const id = String(instanceId || '').trim();
      if (!id) {
        return '';
      }
      const entry = this.ansiReplayByInstance[id];
      if (!entry || !Array.isArray(entry.chunks) || entry.chunks.length === 0) {
        return '';
      }
      return entry.chunks.join('');
    },

    hasServerBaseline(instanceId) {
      const id = String(instanceId || '').trim();
      if (!id) {
        return false;
      }
      const entry = this.ansiReplayByInstance[id];
      return entry?.hasServerBaseline === true;
    },

    buildLocalClearMessage(instanceId) {
      const id = String(instanceId || '').trim();
      if (!id) {
        return null;
      }
      return {
        v: 1,
        type: 'term.raw',
        instance_id: id,
        replay: true,
        reset: true,
        local: true,
        data: ''
      };
    },

    buildLocalReplayMessage(instanceId) {
      const id = String(instanceId || '').trim();
      if (!id) {
        return null;
      }
      const data = this.getAnsiReplayData(id);
      if (!data) {
        return null;
      }
      const stream = this.ensureStreamState(id);
      const seq = parseSeq(stream?.lastSeq, 0);
      return {
        v: 1,
        type: 'term.raw',
        instance_id: id,
        replay: true,
        req_id: `local-replay-${Date.now()}`,
        since_seq: 0,
        from_seq: 1,
        to_seq: seq,
        seq,
        reset: true,
        truncated: false,
        local: true,
        data
      };
    },

    isMessageForSelected(message) {
      if (!message || typeof message !== 'object') {
        return false;
      }
      const instanceId = String(message.instance_id || '').trim();
      if (!instanceId) {
        return true;
      }
      return instanceId === String(this.selectedInstanceId || '').trim();
    },

    cloneFrame(message) {
      try {
        if (typeof structuredClone === 'function') {
          return structuredClone(message);
        }
      } catch {
      }
      return JSON.parse(JSON.stringify(message));
    },

    rememberScreenFrame(message) {
      if (!message || typeof message !== 'object') {
        return;
      }
      const instanceId = String(message.instance_id || '').trim();
      if (!instanceId) {
        return;
      }
      if (message.type === 'term.snapshot') {
        this.cachedScreens[instanceId] = this.cloneFrame(message);
        this.syncStreamSeq(instanceId, parseSeq(message.seq, parseSeq(message.base_seq, 0)));
        return;
      }
      if (message.type !== 'term.patch') {
        return;
      }

      const prev = this.cachedScreens[instanceId];
      if (!prev || prev.type !== 'term.snapshot') {
        return;
      }
      const next = this.cloneFrame(prev);
      if (Array.isArray(message.rows) && Array.isArray(next.rows)) {
        for (const row of message.rows) {
          if (!Number.isFinite(row?.y) || row.y < 0) {
            continue;
          }
          next.rows[row.y] = {
            y: row.y,
            segs: Array.isArray(row.segs) ? row.segs : [['', 0]]
          };
        }
      }
      next.cursor = message.cursor || next.cursor;
      next.seq = message.seq || next.seq;
      this.cachedScreens[instanceId] = next;
      this.syncStreamSeq(instanceId, message.seq);
    },

    ensureStreamState(instanceId) {
      const id = String(instanceId || '').trim();
      if (!id) {
        return null;
      }
      if (!this.streamStates[id]) {
        this.streamStates[id] = {
          lastSeq: 0,
          syncInFlight: false,
          activeReqId: '',
          pendingRaw: [],
          syncTimeout: null
        };
      }
      return this.streamStates[id];
    },

    syncStreamSeq(instanceId, value) {
      const id = String(instanceId || '').trim();
      if (!id) {
        return;
      }
      const seq = parseSeq(value, 0);
      if (seq <= 0) {
        return;
      }
      const stream = this.ensureStreamState(id);
      if (!stream) {
        return;
      }
      if (seq > stream.lastSeq) {
        stream.lastSeq = seq;
      }
    },

    async waitForSnapshot(instanceId, timeoutMs = 500) {
      const id = String(instanceId || '').trim();
      if (!id) {
        return null;
      }
      const current = this.cachedScreens[id];
      if (current?.type === 'term.snapshot') {
        return current;
      }

      const deadline = Date.now() + Math.max(0, Number(timeoutMs) || 0);
      while (Date.now() < deadline) {
        await new Promise((resolve) => setTimeout(resolve, 30));
        const latest = this.cachedScreens[id];
        if (latest?.type === 'term.snapshot') {
          return latest;
        }
      }

      const fallback = this.cachedScreens[id];
      return fallback?.type === 'term.snapshot' ? fallback : null;
    },

    resetStreamState(instanceId, lastSeq = 0) {
      const id = String(instanceId || '').trim();
      if (!id) {
        return;
      }
      this.streamStates[id] = {
        lastSeq: Math.max(0, Number(lastSeq) || 0),
        syncInFlight: false,
        activeReqId: '',
        pendingRaw: [],
        syncTimeout: null
      };
    },

    queuePendingRaw(stream, message) {
      const seq = parseSeq(message?.seq, 0);
      if (seq > 0 && stream.pendingRaw.some((x) => parseSeq(x?.seq, 0) === seq)) {
        return;
      }
      stream.pendingRaw = [...stream.pendingRaw, this.cloneFrame(message)];
    },

    flushPendingRaw(instanceId) {
      const stream = this.ensureStreamState(instanceId);
      if (!stream || stream.pendingRaw.length === 0) {
        return [];
      }

      const sorted = [...stream.pendingRaw].sort((a, b) => parseSeq(a?.seq, 0) - parseSeq(b?.seq, 0));
      const emitted = [];
      const remaining = [];

      for (const message of sorted) {
        const seq = parseSeq(message?.seq, 0);
        if (seq <= 0) {
          emitted.push(message);
          continue;
        }
        if (seq <= stream.lastSeq) {
          continue;
        }
        if (seq === stream.lastSeq + 1) {
          stream.lastSeq = seq;
          emitted.push(message);
          continue;
        }
        remaining.push(message);
      }

      stream.pendingRaw = remaining;
      return emitted;
    },

    async requestRawSync(instanceId, reason = 'manual', options = {}) {
      const id = String(instanceId || '').trim();
      if (!id || !this.connection || !this.wsConnected) {
        return;
      }

      const stream = this.ensureStreamState(id);
      if (!stream || stream.syncInFlight) {
        return;
      }

      stream.syncInFlight = true;
      stream.activeReqId = `raw-sync-${Date.now()}-${Math.floor(Math.random() * 100000)}`;
      if (stream.syncTimeout) {
        clearTimeout(stream.syncTimeout);
      }
      const activeReqId = stream.activeReqId;
      stream.syncTimeout = setTimeout(() => {
        const latest = this.streamStates[id];
        if (!latest || !latest.syncInFlight || latest.activeReqId !== activeReqId) {
          return;
        }
        latest.syncInFlight = false;
        latest.activeReqId = '';
        latest.syncTimeout = null;
        for (const item of this.flushPendingRaw(id)) {
          this.emitMessage(item);
        }
      }, 1500);
      this.emitMessage({
        type: 'term.sync.start',
        instance_id: id,
        reason
      });

      try {
        const explicitSince = Number(options?.sinceSeq);
        const fromStart = options?.fromStart === true;
        const hasExplicitSince = Number.isFinite(explicitSince);
        const sinceSeq = hasExplicitSince
          ? Math.max(0, Math.floor(explicitSince))
          : (fromStart
              ? 0
              : Math.max(0, Number(stream.lastSeq) || 0));
        await this.connection.invoke('RequestSync', {
          instanceId: id,
          type: 'raw',
          sinceSeq,
          reqId: stream.activeReqId
        });
      } catch (error) {
        stream.syncInFlight = false;
        stream.activeReqId = '';
        if (stream.syncTimeout) {
          clearTimeout(stream.syncTimeout);
          stream.syncTimeout = null;
        }
        throw error;
      }
    },

    handleIncomingRaw(message) {
      const id = String(message?.instance_id || '').trim();
      const stream = this.ensureStreamState(id);
      if (!stream) {
        return [message];
      }

      const isReplay = message?.replay === true;
      const seq = parseSeq(message?.seq, 0);
      if (isReplay) {
        const toSeq = parseSeq(message?.to_seq, seq);
        if (toSeq > stream.lastSeq) {
          stream.lastSeq = toSeq;
        }
        if (stream.syncTimeout) {
          clearTimeout(stream.syncTimeout);
          stream.syncTimeout = null;
        }
        stream.syncInFlight = false;
        stream.activeReqId = '';
        if (message?.reset === true) {
          stream.pendingRaw = stream.pendingRaw.filter((x) => parseSeq(x?.seq, 0) > stream.lastSeq);
        }
        return [message, ...this.flushPendingRaw(id)];
      }

      if (seq <= 0) {
        return [message];
      }

      if (stream.syncInFlight) {
        this.queuePendingRaw(stream, message);
        return [];
      }

      if (seq <= stream.lastSeq) {
        return [];
      }

      if (seq > stream.lastSeq + 1) {
        if (id !== String(this.selectedInstanceId || '').trim()) {
          this.queuePendingRaw(stream, message);
          this.requestRawSync(id, 'seq_gap_background').catch(() => {});
          return [];
        }
        this.queuePendingRaw(stream, message);
        if (id === String(this.selectedInstanceId || '').trim()) {
          this.setStatus('Resync requested');
          this.requestRawSync(id, 'seq_gap').catch(() => {
            this.setStatus(`Auto resync failed: ${id}`);
          });
        }
        return [];
      }

      stream.lastSeq = seq;
      return [message];
    },

    handleIncomingSyncComplete(message) {
      const id = String(message?.instance_id || '').trim();
      const stream = this.ensureStreamState(id);
      if (!stream) {
        return [message];
      }

      const reqId = String(message?.req_id || '').trim();
      if (stream.activeReqId && reqId && reqId !== stream.activeReqId) {
        return [];
      }

      const toSeq = parseSeq(message?.to_seq, stream.lastSeq);
      if (toSeq > stream.lastSeq) {
        stream.lastSeq = toSeq;
      }

      stream.syncInFlight = false;
      stream.activeReqId = '';
      if (stream.syncTimeout) {
        clearTimeout(stream.syncTimeout);
        stream.syncTimeout = null;
      }
      const drained = this.flushPendingRaw(id);
      if (stream.pendingRaw.length > 0) {
        this.requestRawSync(id, 'post_sync_gap').catch(() => {
          if (id === String(this.selectedInstanceId || '').trim()) {
            this.setStatus(`Auto resync failed: ${id}`);
          }
        });
      }

      return [message, ...drained];
    },

    shouldSuppressResizeSnapshot(instanceId, message) {
      const id = String(instanceId || '').trim();
      if (!id) {
        return false;
      }
      const ackAt = Number(this.resizeAckByInstance[id] || 0);
      if (!Number.isFinite(ackAt) || ackAt <= 0) {
        return false;
      }
      if (Date.now() - ackAt > 1200) {
        return false;
      }
      const stream = this.ensureStreamState(id);
      if (!stream || parseSeq(stream.lastSeq, 0) <= 0) {
        return false;
      }
      const snapshotSeq = parseSeq(message?.seq, parseSeq(message?.base_seq, 0));
      if (snapshotSeq <= 0) {
        return false;
      }
      if (snapshotSeq > 0 && stream && snapshotSeq > stream.lastSeq + 1) {
        return false;
      }
      return true;
    },

    processIncomingMessage(message) {
      if (!message || typeof message !== 'object') {
        return [];
      }
      const instanceId = String(message.instance_id || '').trim();
      const isSelected = this.isMessageForSelected(message);
      if (message.type === 'term.resize.ack') {
        if (instanceId) {
          this.resizeAckByInstance[instanceId] = Date.now();
        }
        return isSelected ? [message] : [];
      }
      if (message.type === 'term.snapshot' && this.shouldSuppressResizeSnapshot(instanceId, message)) {
        this.rememberScreenFrame(message);
        this.syncStreamSeq(instanceId, parseSeq(message.seq, parseSeq(message.base_seq, 0)));
        return [];
      }
      if (message.type === 'term.raw') {
        return this.handleIncomingRaw(message);
      }
      if (message.type === 'term.sync.complete') {
        return this.handleIncomingSyncComplete(message);
      }
      if (!isSelected && (message.type === 'term.snapshot' || message.type === 'term.patch' || message.type === 'term.exit')) {
        return [message];
      }
      return isSelected ? [message] : [];
    },

    async fetchInstances() {
      const response = await fetch(buildApiPath('/api/instances'));
      if (!response.ok) {
        throw new Error(`load instances failed: ${response.status}`);
      }
      const body = await response.json();
      this.instances = Array.isArray(body?.items) ? body.items : [];
      const activeIds = new Set(this.instances.map((x) => String(x?.id || '').trim()).filter(Boolean));
      this.joinedInstanceIds = this.joinedInstanceIds.filter((id) => activeIds.has(id));
      this.joinedInstanceId = this.joinedInstanceIds[this.joinedInstanceIds.length - 1] || '';
      for (const id of Object.keys(this.streamStates)) {
        if (!activeIds.has(id)) {
          delete this.streamStates[id];
        }
      }
      for (const id of Object.keys(this.cachedScreens)) {
        if (!activeIds.has(id)) {
          delete this.cachedScreens[id];
        }
      }
      for (const id of Object.keys(this.plainOutputByInstance)) {
        if (!activeIds.has(id)) {
          delete this.plainOutputByInstance[id];
        }
      }
      for (const id of Object.keys(this.ansiReplayByInstance)) {
        if (!activeIds.has(id)) {
          delete this.ansiReplayByInstance[id];
        }
      }
      for (const id of Object.keys(this.resizeAckByInstance)) {
        if (!activeIds.has(id)) {
          delete this.resizeAckByInstance[id];
        }
      }
      const routeInstanceId = String(this.terminalResponseRoute?.instanceId || '').trim();
      if (routeInstanceId && !activeIds.has(routeInstanceId)) {
        this.terminalResponseRoute = { instanceId: '', expiresAt: 0 };
      }
      if (this.selectedInstanceId && !this.instances.some((x) => x.id === this.selectedInstanceId)) {
        this.selectedInstanceId = '';
        this.setStatus('Current instance exited');
      }
      this.syncPlainOutputForSelected();
      if (this.connection && this.wsConnected) {
        this.syncJoinedInstances().catch(() => {});
      }
      return this.instances;
    },

    async fetchNodes() {
      const response = await fetch(buildApiPath('/api/nodes'));
      if (!response.ok) {
        throw new Error(`load nodes failed: ${response.status}`);
      }
      const body = await response.json();
      this.nodes = Array.isArray(body?.items) ? body.items : [];
      return this.nodes;
    },

    async fetchProjects() {
      const response = await fetch(buildApiPath('/api/projects'));
      if (!response.ok) {
        throw new Error(`load projects failed: ${response.status}`);
      }
      const body = await response.json();
      this.projects = Array.isArray(body?.items) ? body.items : [];
      return this.projects;
    },

    getDefaultNodeId() {
      const master = this.nodes.find((x) => String(x?.node_role || '').toLowerCase() === 'master');
      if (master?.node_id) {
        return String(master.node_id);
      }
      const first = this.nodes.find((x) => String(x?.node_id || '').trim().length > 0);
      return first?.node_id ? String(first.node_id) : '';
    },

    isLocalNode(nodeId) {
      const normalized = String(nodeId || '').trim();
      if (!normalized) {
        return true;
      }
      const local = this.getDefaultNodeId();
      if (!local) {
        return true;
      }
      return local === normalized;
    },

    scheduleRouteResync(instanceId) {
      const id = String(instanceId || '').trim();
      if (!id || !this.connection || !this.wsConnected || this.selectedInstanceId !== id) {
        return;
      }

      if (this.routeResyncTimers[id]) {
        clearTimeout(this.routeResyncTimers[id]);
      }

      this.routeResyncTimers[id] = setTimeout(async () => {
        delete this.routeResyncTimers[id];
        if (this.routeResyncInFlight[id] || !this.connection || !this.wsConnected || this.selectedInstanceId !== id) {
          return;
        }

        this.routeResyncInFlight[id] = true;
        try {
          await this.requestRawSync(id, 'route_seq_gap');
        } catch {
          this.setStatus(`Auto resync failed: ${id}`);
        } finally {
          delete this.routeResyncInFlight[id];
        }
      }, 300);
    },

    async createInstance(input, nodeId = '') {
      const normalizedNode = String(nodeId || '').trim();
      const endpoint = normalizedNode && !this.isLocalNode(normalizedNode)
        ? buildApiPath(`/api/nodes/${encodeURIComponent(normalizedNode)}/instances`)
        : buildApiPath('/api/instances');
      const response = await fetch(endpoint, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(input)
      });

      if (!response.ok) {
        let message = `create instance failed: ${response.status}`;
        try {
          const payload = await response.json();
          if (payload?.error) {
            message = payload.error;
          }
        } catch {
        }
        throw new Error(message);
      }

      const created = await response.json();
      await this.fetchInstances();
      this.selectedInstanceId = String(created.instance_id || '');
      this.setStatus('Created');
      return created;
    },

    disconnect() {
      if (this.resizeTimer) {
        clearTimeout(this.resizeTimer);
        this.resizeTimer = null;
      }
      this.pendingResize = null;

      if (this.connection) {
        if (this.wsConnected) {
          for (const joinedId of this.joinedInstanceIds) {
            this.connection.invoke('LeaveInstance', { instanceId: joinedId }).catch(() => {});
          }
        }
        this.connection.stop().catch(() => {});
      }
      for (const key of Object.keys(this.routeResyncTimers)) {
        clearTimeout(this.routeResyncTimers[key]);
      }
      this.routeResyncTimers = {};
      this.routeResyncInFlight = {};
      this.resizeAckByInstance = {};
      this.connection = null;
      this.joinedInstanceId = '';
      this.joinedInstanceIds = [];
      for (const stream of Object.values(this.streamStates)) {
        if (stream?.syncTimeout) {
          clearTimeout(stream.syncTimeout);
        }
      }
      this.streamStates = {};
      this.wsConnected = false;
      this.isReconnecting = false;
      this.plainOutput = '';
      this.plainOutputByInstance = {};
      this.ansiReplayByInstance = {};
      this.terminalResponseRoute = { instanceId: '', expiresAt: 0 };
    },

    async leaveJoinedInstance(instanceId, options = {}) {
      const id = String(instanceId || '').trim();
      if (!id) {
        return;
      }
      this.joinedInstanceIds = this.joinedInstanceIds.filter((item) => item !== id);
      this.joinedInstanceId = this.joinedInstanceIds[this.joinedInstanceIds.length - 1] || '';

      const callRemote = options?.callRemote !== false;
      if (!callRemote || !this.connection || !this.wsConnected) {
        return;
      }
      try {
        await this.connection.invoke('LeaveInstance', { instanceId: id });
      } catch {
      }
    },

    async joinInstance(instanceId) {
      const id = String(instanceId || '').trim();
      if (!id || !this.connection || !this.wsConnected) {
        return false;
      }

      if (this.joinedInstanceIds.includes(id)) {
        return true;
      }
      await this.connection.invoke('JoinInstance', { instanceId: id });
      if (!this.joinedInstanceIds.includes(id)) {
        this.joinedInstanceIds = [...this.joinedInstanceIds, id];
      }
      this.joinedInstanceId = id;
      return true;
    },

    async syncJoinedInstances(options = {}) {
      if (!this.connection || !this.wsConnected) {
        return;
      }

      const include = Array.isArray(options?.include) ? options.include : [];
      const requiredSet = new Set(include.map((id) => String(id || '').trim()).filter(Boolean));
      const targetIds = [
        ...this.instances.map((item) => String(item?.id || '').trim()),
        ...include.map((id) => String(id || '').trim())
      ].filter((id, index, array) => id.length > 0 && array.indexOf(id) === index);
      const targetSet = new Set(targetIds);

      const stale = this.joinedInstanceIds.filter((id) => !targetSet.has(id));
      for (const id of stale) {
        await this.leaveJoinedInstance(id, { callRemote: true });
      }

      let requiredJoinError = null;
      for (const id of targetIds) {
        try {
          await this.joinInstance(id);
        } catch (error) {
          if (requiredSet.has(id) && !requiredJoinError) {
            requiredJoinError = error || new Error(`JoinInstance failed: ${id}`);
          }
        }
      }

      const joinedSet = new Set(this.joinedInstanceIds.map((id) => String(id || '').trim()).filter(Boolean));
      this.joinedInstanceIds = targetIds.filter((id) => joinedSet.has(id));
      const selectedId = String(this.selectedInstanceId || '').trim();
      this.joinedInstanceId = joinedSet.has(selectedId) && targetSet.has(selectedId)
        ? selectedId
        : (this.joinedInstanceIds[this.joinedInstanceIds.length - 1] || '');

      if (requiredJoinError) {
        throw requiredJoinError;
      }
    },

    async ensureConnection() {
      if (this.connection) {
        return;
      }

      const connection = createHubConnectionBuilder()
        .withUrl(buildHubUrl())
        .withAutomaticReconnect()
        .configureLogging(signalR.LogLevel.Warning)
        .build();
      this.connection = connection;

      connection.on('TerminalEvent', (message) => {
        const messageInstanceId = String(message?.instance_id || '').trim();
        const isSelectedMessage = !messageInstanceId || messageInstanceId === String(this.selectedInstanceId || '').trim();
        if (message?.type === 'term.exit') {
          if (isSelectedMessage) {
            this.setStatus('Instance exited');
          }
        }
        if (message?.type === 'term.route') {
          const reason = String(message?.reason || '');
          const routeInstanceId = messageInstanceId;
          if (reason === 'seq_gap' && routeInstanceId) {
            if (isSelectedMessage) {
              this.setStatus('Resync requested');
            }
            this.scheduleRouteResync(routeInstanceId);
          }
        }
        if (message?.type === 'error') {
          if (isSelectedMessage) {
            this.setStatus(String(message?.error || message?.message || 'error'));
          }
        }
        for (const item of this.processIncomingMessage(message)) {
          this.emitMessage(item);
        }
      });

      connection.onreconnecting(() => {
        this.wsConnected = false;
        this.isReconnecting = true;
        this.setStatus('Reconnecting...');
      });

      connection.onreconnected(async () => {
        this.wsConnected = true;
        this.isReconnecting = false;
        this.setStatus('Connected');
        try {
          const selected = String(this.selectedInstanceId || '').trim();
          this.joinedInstanceId = '';
          this.joinedInstanceIds = [];
          await this.syncJoinedInstances({ include: selected ? [selected] : [] });
          if (selected) {
            const stream = this.ensureStreamState(selected);
            const snapshot = await this.waitForSnapshot(selected, 600);
            const baselineSeq = parseSeq(snapshot?.seq, parseSeq(snapshot?.base_seq, parseSeq(stream?.lastSeq, 0)));
            if (baselineSeq > 0) {
              await this.requestRawSync(selected, 'reconnect', { sinceSeq: baselineSeq });
            } else {
              await this.requestRawSync(selected, 'reconnect', { fromStart: true });
            }
          }
        } catch (error) {
          this.setStatus(String(error?.message || error || 'reconnect sync failed'));
        }
      });

      connection.onclose(() => {
        this.wsConnected = false;
        this.isReconnecting = false;
        this.connection = null;
        this.setStatus('disconnected');
      });

      try {
        await connection.start();
        this.wsConnected = true;
        this.syncJoinedInstances().catch(() => {});
      } catch (error) {
        this.wsConnected = false;
        this.isReconnecting = false;
        this.setStatus(String(error?.message || error || 'connect failed'));
      }
    },

    async connect(instanceId) {
      const nextId = String(instanceId || '').trim();
      if (!nextId) {
        return;
      }

      await this.ensureConnection();
      if (!this.connection || !this.wsConnected) {
        return;
      }

      this.selectedInstanceId = nextId;
      this.ensureStreamState(nextId);
      this.syncPlainOutputForSelected();
      const clearMessage = this.buildLocalClearMessage(nextId);
      if (clearMessage) {
        this.emitMessage(clearMessage);
      }
      const cachedSnapshot = this.cachedScreens[nextId];
      if (cachedSnapshot?.type === 'term.snapshot') {
        this.emitMessage(this.cloneFrame(cachedSnapshot));
      }

      let lastError = null;
      for (let attempt = 0; attempt < 3; attempt += 1) {
        try {
          await this.joinInstance(nextId);
          this.syncJoinedInstances({ include: [nextId] }).catch(() => {});
          const stream = this.ensureStreamState(nextId);
          const liveSnapshot = this.cachedScreens[nextId];
          const snapshot = liveSnapshot?.type === 'term.snapshot'
            ? liveSnapshot
            : await this.waitForSnapshot(nextId, 250);
          const baselineSeq = parseSeq(snapshot?.seq, parseSeq(snapshot?.base_seq, parseSeq(stream?.lastSeq, 0)));
          if (baselineSeq > 0) {
            await this.requestRawSync(nextId, 'connect', { sinceSeq: baselineSeq });
            this.setStatus('Connected');
            return;
          }

          const localReplay = this.buildLocalReplayMessage(nextId);
          if (localReplay) {
            this.emitMessage(localReplay);
            this.setStatus('Connected (local fallback)');
          }

          await this.requestRawSync(nextId, 'connect', { fromStart: true });
          this.setStatus('Connected');
          return;
        } catch (error) {
          lastError = error;
          if (attempt < 2) {
            await new Promise((resolve) => setTimeout(resolve, 120));
          }
        }
      }

      throw lastError || new Error(`connect failed: ${nextId}`);
    },

    async sendInput(data) {
      if (!this.connection || !this.wsConnected) {
        return;
      }
      const body = String(data || '');
      const targetInstanceId = this.resolveInputTargetInstanceId(body);
      if (!targetInstanceId) {
        return;
      }

      await this.connection.invoke('SendInput', {
        instanceId: targetInstanceId,
        data: body
      });
    },

    async sendBracketedPaste(text) {
      const body = String(text || '');
      if (!body) {
        return;
      }
      await this.sendInput(`\u001b[200~${body}\u001b[201~`);
    },

    sendResize(cols, rows) {
      if (!this.connection || !this.selectedInstanceId || !this.wsConnected) {
        return;
      }

      this.pendingResize = {
        cols: Math.max(1, Number(cols) || 1),
        rows: Math.max(1, Number(rows) || 1)
      };

      if (this.resizeTimer) {
        clearTimeout(this.resizeTimer);
      }

      this.resizeTimer = setTimeout(async () => {
        const next = this.pendingResize;
        this.resizeTimer = null;
        this.pendingResize = null;
        if (!next || !this.connection || !this.wsConnected) {
          return;
        }

        await this.connection.invoke('RequestResize', {
          instanceId: this.selectedInstanceId,
          cols: next.cols,
          rows: next.rows,
          reqId: `resize-${Date.now()}`
        });
      }, 200);
    },

    async resync() {
      if (!this.connection || !this.selectedInstanceId || !this.wsConnected) {
        return;
      }

      await this.requestRawSync(this.selectedInstanceId, 'manual');
    },

    async terminateSelected() {
      const id = String(this.selectedInstanceId || '').trim();
      if (!id) {
        return;
      }
      const response = await fetch(buildApiPath(`/api/instances/${encodeURIComponent(id)}`), {
        method: 'DELETE'
      });
      if (!response.ok) {
        throw new Error(`terminate failed: ${response.status}`);
      }
      this.disconnect();
      await this.fetchInstances();
      this.setStatus(`Terminated ${id}`);
    },

    async uploadImageToSelected(file, options = {}) {
      const target = this.selectedInstance;
      if (!target || !file) {
        throw new Error('instance and file are required');
      }
      const nodeId = String(target.node_id || '').trim();
      if (!nodeId) {
        throw new Error('node_id is missing on selected instance');
      }

      const form = new FormData();
      form.append('file', file);
      form.append('instance_id', target.id);

      const endpoint = buildApiPath(`/api/nodes/${encodeURIComponent(nodeId)}/files/upload`);
      const response = await fetch(endpoint, { method: 'POST', body: form });
      if (!response.ok) {
        const text = await response.text();
        throw new Error(text || `upload failed: ${response.status}`);
      }

      const payload = await response.json();
      const path = String(payload?.upload?.path || '').trim();
      if (!path) {
        throw new Error('upload succeeded but no path returned');
      }

      const pressEnter = options?.pressEnter === true;
      await this.sendInput(pressEnter ? `${path}\r` : path);
      this.setStatus(`Uploaded to ${path}`);
      return payload;
    }
  }
});
