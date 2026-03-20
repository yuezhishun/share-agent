import { defineStore } from 'pinia';
import * as signalR from '@microsoft/signalr';

const INSTANCE_ALIAS_STORAGE_KEY = 'webcli-instance-aliases-v1';

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

    getDefaultNodeId() {
      const master = this.nodes.find((item) => String(item?.node_role || '').toLowerCase() === 'master');
      if (master?.node_id) {
        return String(master.node_id);
      }
      return String(this.nodes[0]?.node_id || '');
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
        const messageInstanceId = String(message?.instance_id || '').trim();
        const isSelected = !messageInstanceId || messageInstanceId === String(this.selectedInstanceId || '').trim();
        if (message?.type === 'term.v2.sync.required' && isSelected) {
          this.setStatus(`Resync required: ${String(message?.reason || 'screen_untrusted')}`);
        }
        if (message?.type === 'term.v2.resize.ack' && isSelected && message.accepted === true) {
          this.setStatus('Resizing...');
        }
        if (message?.type === 'term.v2.snapshot' && isSelected) {
          this.setStatus('Connected');
        }
        if (message?.type === 'term.exit' && isSelected) {
          this.setStatus('Instance exited');
        }
        if (isSelected) {
          this.emitMessage(message);
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

    async requestScreenSync(instanceId) {
      const id = String(instanceId || '').trim();
      if (!id || !this.connection || !this.wsConnected) {
        return;
      }

      await this.connection.invoke('RequestSync', {
        instanceId: id,
        type: 'screen'
      });
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
        throw new Error(`create instance failed: ${response.status}`);
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
        ? buildApiPath(`/api/nodes/${encodeURIComponent(nodeId)}/instances/${encodeURIComponent(id)}`)
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
      this.connection = null;
      this.joinedInstanceIds = [];
      this.wsConnected = false;
      this.isReconnecting = false;
      this.setStatus('disconnected');
    }
  }
});
