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

export const useWebCliTerminalStore = defineStore('webcliTerminal', {
  state: () => ({
    instances: [],
    nodes: [],
    projects: [],
    selectedInstanceId: '',
    selectedProjectPath: '',
    status: 'Ready',
    wsConnected: false,
    connection: null,
    joinedInstanceId: '',
    listeners: [],
    resizeTimer: null,
    pendingResize: null,
    cachedScreens: {},
    routeResyncTimers: {},
    routeResyncInFlight: {}
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
      for (const listener of this.listeners) {
        listener(message);
      }
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
    },

    async fetchInstances() {
      const response = await fetch(buildApiPath('/api/instances'));
      if (!response.ok) {
        throw new Error(`load instances failed: ${response.status}`);
      }
      const body = await response.json();
      this.instances = Array.isArray(body?.items) ? body.items : [];
      if (this.selectedInstanceId && !this.instances.some((x) => x.id === this.selectedInstanceId)) {
        this.selectedInstanceId = '';
        this.wsConnected = false;
        this.setStatus('Current instance exited');
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
          await this.connection.invoke('RequestSync', { instanceId: id, type: 'screen' });
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
      this.setStatus(`Created ${this.selectedInstanceId}`);
      return created;
    },

    disconnect() {
      if (this.resizeTimer) {
        clearTimeout(this.resizeTimer);
        this.resizeTimer = null;
      }
      this.pendingResize = null;

      if (this.connection) {
        if (this.selectedInstanceId && this.wsConnected) {
          this.connection.invoke('LeaveInstance', { instanceId: this.selectedInstanceId }).catch(() => {});
        }
        this.connection.stop().catch(() => {});
      }
      for (const key of Object.keys(this.routeResyncTimers)) {
        clearTimeout(this.routeResyncTimers[key]);
      }
      this.routeResyncTimers = {};
      this.routeResyncInFlight = {};
      this.connection = null;
      this.joinedInstanceId = '';
      this.wsConnected = false;
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
        this.emitMessage(message);
        if (message?.type === 'term.exit') {
          this.wsConnected = false;
          this.setStatus('Instance exited');
        }
        if (message?.type === 'term.route') {
          const reason = String(message?.reason || '');
          const routeInstanceId = String(message?.instance_id || '');
          if (reason === 'seq_gap' && routeInstanceId) {
            this.setStatus(`Resync requested: ${routeInstanceId}`);
            this.scheduleRouteResync(routeInstanceId);
          }
        }
        if (message?.type === 'error') {
          this.setStatus(String(message?.error || message?.message || 'error'));
        }
      });

      connection.onreconnecting(() => {
        this.wsConnected = false;
        this.setStatus('Reconnecting...');
      });

      connection.onreconnected(async () => {
        this.wsConnected = true;
        this.setStatus(`Connected: ${this.selectedInstanceId}`);
        try {
          await connection.invoke('JoinInstance', { instanceId: this.selectedInstanceId });
          this.joinedInstanceId = this.selectedInstanceId;
          await connection.invoke('RequestSync', { instanceId: this.selectedInstanceId, type: 'screen' });
        } catch (error) {
          this.setStatus(String(error?.message || error || 'reconnect sync failed'));
        }
      });

      connection.onclose(() => {
        this.wsConnected = false;
        this.joinedInstanceId = '';
      });

      try {
        await connection.start();
        this.wsConnected = true;
      } catch (error) {
        this.wsConnected = false;
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

      const previousId = String(this.joinedInstanceId || '').trim();
      this.selectedInstanceId = nextId;

      const cached = this.cachedScreens[nextId];
      if (cached) {
        this.emitMessage(this.cloneFrame(cached));
      }

      if (previousId && previousId !== nextId) {
        await this.connection.invoke('LeaveInstance', { instanceId: previousId });
      }

      let connected = false;
      let lastError = null;
      for (let attempt = 0; attempt < 3; attempt += 1) {
        try {
          if (previousId !== nextId || this.joinedInstanceId !== nextId) {
            await this.connection.invoke('JoinInstance', { instanceId: nextId });
            this.joinedInstanceId = nextId;
          }
          await this.connection.invoke('RequestSync', { instanceId: nextId, type: 'screen' });
          connected = true;
          break;
        } catch (error) {
          lastError = error;
          this.joinedInstanceId = '';
          if (attempt < 2) {
            await new Promise((resolve) => setTimeout(resolve, 300));
          }
        }
      }

      if (!connected) {
        throw lastError || new Error(`connect failed: ${nextId}`);
      }

      this.setStatus(`Connected: ${nextId}`);
    },

    async sendInput(data) {
      if (!this.connection || !this.selectedInstanceId || !this.wsConnected) {
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

      await this.connection.invoke('RequestSync', {
        instanceId: this.selectedInstanceId,
        type: 'screen'
      });
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
