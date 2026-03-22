import { defineStore } from 'pinia';
import * as signalR from '@microsoft/signalr';

const INSTANCE_ALIAS_STORAGE_KEY = 'webcli-instance-aliases-v1';
const RAW_SYNC_TIMEOUT_MS = 1500;
const SCREEN_SYNC_TIMEOUT_MS = 1500;

function readInstanceAliases() {
  if (typeof window === 'undefined') {
    return {};
  }
  try {
    const storage = window.localStorage;
    if (!storage) {
      return {};
    }
    const raw = storage.getItem(INSTANCE_ALIAS_STORAGE_KEY);
    const parsed = raw ? JSON.parse(raw) : {};
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
      return {};
    }
    return Object.fromEntries(
      Object.entries(parsed)
        .map(([key, value]) => [String(key || '').trim(), String(value || '').trim()])
        .filter(([key, value]) => key.length > 0 && value.length > 0)
    );
  } catch {
    return {};
  }
}

function persistInstanceAliases(aliases) {
  if (typeof window === 'undefined') {
    return;
  }
  try {
    const storage = window.localStorage;
    if (!storage) {
      return;
    }
    storage.setItem(INSTANCE_ALIAS_STORAGE_KEY, JSON.stringify(aliases || {}));
  } catch {
  }
}

function resolveHttpBase() {
  const explicit = String(import.meta.env.VITE_WEBPTY_BASE || '/web-pty').trim();
  return explicit;
}

function buildApiPath(pathname) {
  const base = resolveHttpBase();
  return `${base}${pathname}`;
}

function buildHubUrl() {
  const explicit = String(import.meta.env.VITE_WEBPTY_HUB_URL_V2 || '').trim();
  if (explicit) {
    return explicit;
  }

  const hubPath = String(import.meta.env.VITE_WEBPTY_HUB_PATH_V2 || '/hubs/terminal-v2').trim() || '/hubs/terminal-v2';
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

function cloneMessage(message) {
  try {
    if (typeof structuredClone === 'function') {
      return structuredClone(message);
    }
  } catch {
  }
  return JSON.parse(JSON.stringify(message));
}

async function parseErrorMessage(response, fallback) {
  const text = await response.text();
  if (!text) {
    return fallback;
  }
  try {
    const payload = JSON.parse(text);
    return String(payload?.error || text);
  } catch {
    return text;
  }
}

export const useWebCliTerminalStoreV2 = defineStore('webcliTerminalV2', {
  state: () => ({
    instances: [],
    instanceAliases: readInstanceAliases(),
    nodes: [],
    selectedInstanceId: '',
    status: 'Ready',
    wsConnected: false,
    isReconnecting: false,
    connection: null,
    listeners: [],
    joinedInstanceIds: [],
    resizeTimer: null,
    pendingResize: null,
    streamStates: {},
    resizeAckByInstance: {},
    uiSession: {
      activeRightTab: 'files'
    }
  }),

  getters: {
    selectedInstance(state) {
      return state.instances.find((item) => item.id === state.selectedInstanceId) || null;
    }
  },

  actions: {
    setStatus(value) {
      this.status = String(value || 'Ready');
    },

    getInstanceAlias(instanceId) {
      const id = String(instanceId || '').trim();
      if (!id) {
        return '';
      }
      return String(this.instanceAliases[id] || '').trim();
    },

    setInstanceAlias(instanceId, alias) {
      const id = String(instanceId || '').trim();
      if (!id) {
        return;
      }

      const normalizedAlias = String(alias || '').trim();
      const nextAliases = { ...this.instanceAliases };
      if (normalizedAlias) {
        nextAliases[id] = normalizedAlias;
      } else {
        delete nextAliases[id];
      }

      this.instanceAliases = nextAliases;
      persistInstanceAliases(this.instanceAliases);
    },

    clearInstanceAlias(instanceId) {
      this.setInstanceAlias(instanceId, '');
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
        this.listeners = this.listeners.filter((item) => item !== listener);
      };
    },

    emitMessage(message) {
      for (const listener of this.listeners) {
        listener(message);
      }
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
          syncTimeout: null,
          screenSyncInFlight: false,
          screenSyncReqId: '',
          screenSyncTimeout: null
        };
      }
      return this.streamStates[id];
    },

    syncStreamSeq(instanceId, seq) {
      const stream = this.ensureStreamState(instanceId);
      const next = parseSeq(seq, 0);
      if (!stream || next <= 0) {
        return;
      }
      if (next > stream.lastSeq) {
        stream.lastSeq = next;
      }
    },

    clearScreenSync(instanceId) {
      const stream = this.ensureStreamState(instanceId);
      if (!stream) {
        return;
      }
      stream.screenSyncInFlight = false;
      stream.screenSyncReqId = '';
      if (stream.screenSyncTimeout) {
        clearTimeout(stream.screenSyncTimeout);
        stream.screenSyncTimeout = null;
      }
    },

    markSnapshotReady(instanceId, message) {
      this.syncStreamSeq(instanceId, parseSeq(message?.seq, parseSeq(message?.base_seq, 0)));
      this.clearScreenSync(instanceId);
    },

    queuePendingRaw(stream, message) {
      const seq = parseSeq(message?.seq, 0);
      if (seq <= 0 || stream.pendingRaw.some((item) => parseSeq(item?.seq, 0) === seq)) {
        return;
      }
      stream.pendingRaw = [...stream.pendingRaw, cloneMessage(message)];
    },

    flushPendingRaw(instanceId) {
      const stream = this.ensureStreamState(instanceId);
      if (!stream || stream.pendingRaw.length === 0) {
        return [];
      }

      const sorted = [...stream.pendingRaw].sort((left, right) => parseSeq(left?.seq, 0) - parseSeq(right?.seq, 0));
      const emitted = [];
      const remaining = [];
      for (const item of sorted) {
        const seq = parseSeq(item?.seq, 0);
        if (seq <= 0) {
          emitted.push(item);
          continue;
        }
        if (seq <= stream.lastSeq) {
          continue;
        }
        if (seq === stream.lastSeq + 1) {
          stream.lastSeq = seq;
          emitted.push(item);
          continue;
        }
        remaining.push(item);
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
      stream.activeReqId = `raw-sync-v2-${Date.now()}-${Math.floor(Math.random() * 100000)}`;
      if (stream.syncTimeout) {
        clearTimeout(stream.syncTimeout);
      }
      const activeReqId = stream.activeReqId;
      stream.syncTimeout = setTimeout(() => {
        const current = this.streamStates[id];
        if (!current || !current.syncInFlight || current.activeReqId !== activeReqId) {
          return;
        }
        current.syncInFlight = false;
        current.activeReqId = '';
        current.syncTimeout = null;
        this.requestScreenSync(id, `${reason}_timeout`).catch(() => {});
      }, RAW_SYNC_TIMEOUT_MS);

      const requestedSinceSeq = Number(options?.sinceSeq);
      const sinceSeq = Number.isFinite(requestedSinceSeq)
        ? Math.max(0, Math.floor(requestedSinceSeq))
        : Math.max(0, Number(stream.lastSeq) || 0);

      await this.connection.invoke('RequestSync', {
        instanceId: id,
        type: 'raw',
        reqId: stream.activeReqId,
        sinceSeq
      });
    },

    async requestScreenSync(instanceId, reason = 'manual') {
      const id = String(instanceId || '').trim();
      if (!id || !this.connection || !this.wsConnected) {
        return;
      }

      const stream = this.ensureStreamState(id);
      if (stream?.screenSyncInFlight) {
        return;
      }

      if (stream) {
        stream.screenSyncInFlight = true;
        stream.screenSyncReqId = `screen-sync-v2-${Date.now()}-${Math.floor(Math.random() * 100000)}`;
        if (stream.screenSyncTimeout) {
          clearTimeout(stream.screenSyncTimeout);
        }
        const activeReqId = stream.screenSyncReqId;
        stream.screenSyncTimeout = setTimeout(() => {
          const current = this.streamStates[id];
          if (!current || !current.screenSyncInFlight || current.screenSyncReqId !== activeReqId) {
            return;
          }
          current.screenSyncInFlight = false;
          current.screenSyncReqId = '';
          current.screenSyncTimeout = null;
          this.setStatus(`Resync timeout: ${reason}`);
        }, SCREEN_SYNC_TIMEOUT_MS);
      }

      await this.connection.invoke('RequestSync', {
        instanceId: id,
        type: 'screen',
        reqId: stream?.screenSyncReqId || `screen-sync-v2-${Date.now()}`
      });
    },

    async fallbackToScreenSync(instanceId, reason) {
      try {
        await this.requestScreenSync(instanceId, reason);
      } catch {
        this.setStatus(`Auto resync failed: ${String(instanceId || '')}`);
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
        return [message];
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
        this.queuePendingRaw(stream, message);
        this.requestRawSync(id, 'seq_gap').catch(() => {
          this.fallbackToScreenSync(id, 'seq_gap').catch(() => {});
        });
        return [];
      }

      stream.lastSeq = seq;
      return [message];
    },

    handleIncomingSyncComplete(message) {
      const id = String(message?.instance_id || '').trim();
      const stream = this.ensureStreamState(id);
      if (!stream) {
        return [];
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
          this.fallbackToScreenSync(id, 'post_sync_gap').catch(() => {});
        });
      }
      return drained;
    },

    processIncomingMessage(message) {
      if (!message || typeof message !== 'object') {
        return [];
      }

      const instanceId = String(message?.instance_id || '').trim();
      if (message.type === 'term.v2.snapshot') {
        this.markSnapshotReady(instanceId, message);
        return [message];
      }

      if (message.type === 'term.v2.raw') {
        return this.handleIncomingRaw(message);
      }

      if (message.type === 'term.v2.sync.complete') {
        return this.handleIncomingSyncComplete(message);
      }

      if (message.type === 'term.v2.sync.required') {
        const stream = this.ensureStreamState(instanceId);
        const hasSeq = parseSeq(stream?.lastSeq, 0) > 0;
        const request = hasSeq
          ? this.requestRawSync(instanceId, String(message.reason || 'screen_untrusted'))
          : this.requestScreenSync(instanceId, String(message.reason || 'screen_untrusted'));
        request.catch(() => {
          this.fallbackToScreenSync(instanceId, String(message.reason || 'screen_untrusted')).catch(() => {});
        });
        return [message];
      }

      if (message.type === 'term.v2.resize.ack' && message.accepted === true) {
        this.resizeAckByInstance[instanceId] = Date.now();
        this.requestScreenSync(instanceId, 'resize').catch(() => {
          this.setStatus(`Auto resync failed: ${instanceId}`);
        });
        return [message];
      }

      return [message];
    },

    updateStatusFromMessage(message) {
      const messageInstanceId = String(message?.instance_id || '').trim();
      const isSelected = !messageInstanceId || messageInstanceId === String(this.selectedInstanceId || '').trim();
      if (!isSelected) {
        return;
      }
      if (message?.type === 'term.v2.sync.required') {
        this.setStatus(`Resync required: ${String(message?.reason || 'screen_untrusted')}`);
        return;
      }
      if (message?.type === 'term.v2.resize.ack' && message.accepted === true) {
        this.setStatus('Resizing...');
        return;
      }
      if (message?.type === 'term.v2.snapshot') {
        this.setStatus('Connected');
        return;
      }
      if (message?.type === 'term.exit') {
        this.setStatus('Instance exited');
      }
    },

    async fetchInstances() {
      const response = await fetch(buildApiPath('/api/v2/instances'));
      if (!response.ok) {
        throw new Error(`load instances failed: ${response.status}`);
      }
      const body = await response.json();
      this.instances = Array.isArray(body?.items) ? body.items : [];
      if (this.selectedInstanceId && !this.instances.some((item) => item.id === this.selectedInstanceId)) {
        this.selectedInstanceId = '';
      }
      const activeIds = new Set(this.instances.map((item) => String(item?.id || '').trim()).filter(Boolean));
      for (const id of Object.keys(this.streamStates)) {
        if (!activeIds.has(id)) {
          const stream = this.streamStates[id];
          if (stream?.syncTimeout) {
            clearTimeout(stream.syncTimeout);
          }
          if (stream?.screenSyncTimeout) {
            clearTimeout(stream.screenSyncTimeout);
          }
          delete this.streamStates[id];
          delete this.resizeAckByInstance[id];
        }
      }
      return this.instances;
    },

    async fetchNodes() {
      const response = await fetch(buildApiPath('/api/v2/nodes'));
      if (!response.ok) {
        throw new Error(`load nodes failed: ${response.status}`);
      }
      const body = await response.json();
      this.nodes = Array.isArray(body?.items) ? body.items : [];
      return this.nodes;
    },

    resolvePreferredNodeId(candidate = '') {
      const normalizedCandidate = String(candidate || '').trim();
      if (normalizedCandidate && this.nodes.some((item) => String(item?.node_id || '').trim() === normalizedCandidate)) {
        return normalizedCandidate;
      }

      const onlineCurrent = this.nodes.find((item) => item?.is_current === true && item?.node_online !== false);
      if (onlineCurrent?.node_id) {
        return String(onlineCurrent.node_id);
      }

      const onlineMaster = this.nodes.find((item) =>
        String(item?.node_role || '').toLowerCase() === 'master' && item?.node_online !== false
      );
      if (onlineMaster?.node_id) {
        return String(onlineMaster.node_id);
      }

      const currentNode = this.nodes.find((item) => item?.is_current === true);
      if (currentNode?.node_id) {
        return String(currentNode.node_id);
      }

      return String(this.nodes[0]?.node_id || '');
    },

    getDefaultNodeId(candidate = '') {
      return this.resolvePreferredNodeId(candidate);
    },

    async joinInstance(instanceId) {
      const id = String(instanceId || '').trim();
      if (!id || !this.connection || !this.wsConnected) {
        return;
      }
      if (!this.joinedInstanceIds.includes(id)) {
        await this.connection.invoke('JoinInstance', { instanceId: id });
        this.joinedInstanceIds = [...this.joinedInstanceIds, id];
      }
    },

    async syncJoinedInstances(options = {}) {
      if (!this.connection || !this.wsConnected) {
        return;
      }

      const include = Array.isArray(options?.include) ? options.include : [];
      const targetIds = [
        ...this.instances.map((item) => String(item?.id || '').trim()),
        ...include.map((id) => String(id || '').trim())
      ].filter((id, index, array) => id.length > 0 && array.indexOf(id) === index);

      const stale = this.joinedInstanceIds.filter((id) => !targetIds.includes(id));
      for (const id of stale) {
        try {
          await this.connection.invoke('LeaveInstance', { instanceId: id });
        } catch {
        }
      }

      for (const id of targetIds) {
        try {
          await this.joinInstance(id);
        } catch {
        }
      }
      this.joinedInstanceIds = targetIds.filter((id) => this.joinedInstanceIds.includes(id));
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
        const processed = this.processIncomingMessage(message);
        for (const item of processed) {
          this.updateStatusFromMessage(item);
          const messageInstanceId = String(item?.instance_id || '').trim();
          const isSelected = !messageInstanceId || messageInstanceId === String(this.selectedInstanceId || '').trim();
          if (isSelected) {
            this.emitMessage(item);
          }
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
        const selected = String(this.selectedInstanceId || '').trim();
        this.joinedInstanceIds = [];
        await this.syncJoinedInstances({ include: selected ? [selected] : [] });
        if (selected) {
          await this.requestScreenSync(selected);
        }
      });

      connection.onclose(() => {
        this.wsConnected = false;
        this.isReconnecting = false;
        this.connection = null;
        this.joinedInstanceIds = [];
        for (const id of Object.keys(this.streamStates)) {
          const stream = this.streamStates[id];
          if (stream?.syncTimeout) {
            clearTimeout(stream.syncTimeout);
          }
          if (stream?.screenSyncTimeout) {
            clearTimeout(stream.screenSyncTimeout);
          }
        }
        this.streamStates = {};
        this.setStatus('disconnected');
      });

      await connection.start();
      this.wsConnected = true;
      this.setStatus('Connected');
    },

    async connect(instanceId) {
      const id = String(instanceId || '').trim();
      if (!id) {
        return;
      }

      await this.ensureConnection();
      this.selectedInstanceId = id;
      await this.joinInstance(id);
      await this.syncJoinedInstances({ include: [id] });
      await this.requestScreenSync(id);
      this.setStatus('Connected');
    },

    async sendInput(data) {
      if (!this.connection || !this.wsConnected || !this.selectedInstanceId) {
        return;
      }
      await this.connection.invoke('SendInput', {
        instanceId: this.selectedInstanceId,
        data: String(data || '')
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
      if (!this.connection || !this.wsConnected || !this.selectedInstanceId) {
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
        this.pendingResize = null;
        this.resizeTimer = null;
        if (!next || !this.connection || !this.wsConnected || !this.selectedInstanceId) {
          return;
        }

        await this.connection.invoke('RequestResize', {
          instanceId: this.selectedInstanceId,
          cols: next.cols,
          rows: next.rows,
          reqId: `resize-v2-${Date.now()}`
        });
      }, 200);
    },

    async resync() {
      await this.requestScreenSync(this.selectedInstanceId);
    },

    async createInstance(input, nodeId = '') {
      const normalizedNode = String(nodeId || '').trim();
      const endpoint = normalizedNode
        ? buildApiPath(`/api/v2/nodes/${encodeURIComponent(normalizedNode)}/instances`)
        : buildApiPath('/api/v2/instances');
      const response = await fetch(endpoint, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(input)
      });
      if (!response.ok) {
        throw new Error(await parseErrorMessage(response, `create instance failed: ${response.status}`));
      }
      const created = await response.json();
      await this.fetchInstances();
      this.selectedInstanceId = String(created.instance_id || '');
      return created;
    },

    async terminateSelected() {
      return this.terminateInstance(this.selectedInstanceId);
    },

    async terminateInstance(instanceId) {
      const id = String(instanceId || '').trim();
      if (!id) {
        return;
      }
      const target = this.instances.find((item) => String(item?.id || '') === id) || this.selectedInstance;
      const nodeId = String(target?.node_id || '').trim();
      const endpoint = nodeId
        ? buildApiPath(`/api/v2/nodes/${encodeURIComponent(nodeId)}/instances/${encodeURIComponent(id)}`)
        : buildApiPath(`/api/v2/instances/${encodeURIComponent(id)}`);
      const response = await fetch(endpoint, {
        method: 'DELETE'
      });
      if (!response.ok) {
        throw new Error(`terminate failed: ${response.status}`);
      }
      await this.fetchInstances();
      if (this.selectedInstanceId === id) {
        this.selectedInstanceId = this.instances[0]?.id || '';
      }
      this.setStatus(`Terminated ${id}`);
    },

    disconnect() {
      if (this.resizeTimer) {
        clearTimeout(this.resizeTimer);
      }
      this.pendingResize = null;
      this.resizeTimer = null;
      if (this.connection) {
        this.connection.stop().catch(() => {});
      }
      for (const id of Object.keys(this.streamStates)) {
        const stream = this.streamStates[id];
        if (stream?.syncTimeout) {
          clearTimeout(stream.syncTimeout);
        }
        if (stream?.screenSyncTimeout) {
          clearTimeout(stream.screenSyncTimeout);
        }
      }
      this.connection = null;
      this.joinedInstanceIds = [];
      this.streamStates = {};
      this.resizeAckByInstance = {};
      this.wsConnected = false;
      this.isReconnecting = false;
      this.setStatus('disconnected');
    }
  }
});
