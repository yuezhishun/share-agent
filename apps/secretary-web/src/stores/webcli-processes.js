import { defineStore } from 'pinia';
import { parseCommandLine } from '../utils/command-line.js';

function resolveHttpBase() {
  return String(import.meta.env?.VITE_WEBPTY_BASE || '/web-pty').trim();
}

function buildApiPath(pathname, params) {
  const base = resolveHttpBase();
  const url = new URL(`${base}${pathname}`, typeof window !== 'undefined' ? window.location.origin : 'http://127.0.0.1');
  if (params) {
    for (const [key, value] of Object.entries(params)) {
      if (value === undefined || value === null || value === '') {
        continue;
      }
      url.searchParams.set(key, String(value));
    }
  }
  return `${url.pathname}${url.search}`;
}

function buildNodeProcessesPath(nodeId, suffix = '', params) {
  const normalizedNodeId = String(nodeId || '').trim();
  if (!normalizedNodeId) {
    throw new Error('node is required');
  }
  return buildApiPath(`/api/nodes/${encodeURIComponent(normalizedNodeId)}/processes${suffix}`, params);
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

function buildDefaultForm() {
  return {
    commandLine: '',
    cwd: '/home/yueyuan',
    envInput: '{}',
    stdin: '',
    timeoutMs: '300000',
    allowNonZeroExitCode: true
  };
}

function parseEnvInput(input) {
  const raw = String(input || '').trim();
  if (!raw) {
    return {};
  }

  const parsed = JSON.parse(raw);
  if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
    throw new Error('环境变量必须是 JSON 对象');
  }

  return Object.fromEntries(
    Object.entries(parsed).map(([key, value]) => [key, String(value ?? '')])
  );
}

function normalizeTimeout(input) {
  const raw = String(input || '').trim();
  if (!raw) {
    return null;
  }

  const value = Number(raw);
  if (!Number.isFinite(value) || value <= 0) {
    throw new Error('超时时间必须是正整数毫秒');
  }
  return Math.round(value);
}

export const useWebCliProcessesStore = defineStore('webcliProcesses', {
  state: () => ({
    form: buildDefaultForm(),
    nodes: [],
    selectedNodeId: '',
    items: [],
    selectedProcessId: '',
    selectedProcess: null,
    outputItems: [],
    status: '',
    error: '',
    loadingNodes: false,
    loadingList: false,
    loadingDetails: false,
    loadingOutput: false,
    submitting: false,
    waiting: false,
    stopping: false,
    removing: false,
    removingProcessId: '',
    pollTimer: null
  }),

  getters: {
    selectedNode(state) {
      return state.nodes.find((item) => String(item?.node_id || '') === String(state.selectedNodeId || '')) || null;
    },

    isSelectedNodeOnline() {
      return this.selectedNode?.node_online !== false;
    },

    selectedProcessStatus(state) {
      return String(state.selectedProcess?.status || '').trim().toLowerCase();
    },

    hasSelectedProcess(state) {
      return String(state.selectedProcessId || '').trim().length > 0;
    },

    sortedItems(state) {
      return [...state.items].sort((a, b) => {
        const left = new Date(a?.startTime || 0).getTime();
        const right = new Date(b?.startTime || 0).getTime();
        return right - left;
      });
    }
  },

  actions: {
    setStatus(value) {
      this.status = String(value || '');
    },

    setError(value) {
      this.error = String(value || '');
    },

    resetForm() {
      this.form = buildDefaultForm();
    },

    buildRunRequest() {
      const { command, args } = parseCommandLine(this.form.commandLine);

      const body = {
        file: command,
        args,
        cwd: String(this.form.cwd || '').trim() || undefined,
        env: parseEnvInput(this.form.envInput),
        stdin: String(this.form.stdin || ''),
        timeoutMs: normalizeTimeout(this.form.timeoutMs),
        allowNonZeroExitCode: this.form.allowNonZeroExitCode === true,
        metadata: {
          source: 'processes-view',
          target_node_id: String(this.selectedNodeId || '').trim()
        }
      };

      return body;
    },

    resolvePreferredNodeId(candidate = '') {
      const normalizedCandidate = String(candidate || '').trim();
      const onlineNodes = this.nodes.filter((item) => item?.node_online !== false);
      const master = onlineNodes.find((item) => String(item?.node_role || '').toLowerCase() === 'master');
      if (normalizedCandidate && this.nodes.some((item) => String(item?.node_id || '') === normalizedCandidate)) {
        return normalizedCandidate;
      }
      if (master?.node_id) {
        return String(master.node_id);
      }
      if (onlineNodes[0]?.node_id) {
        return String(onlineNodes[0].node_id);
      }
      return String(this.nodes[0]?.node_id || '');
    },

    ensureSelectedNode() {
      const nodeId = this.resolvePreferredNodeId(this.selectedNodeId);
      this.selectedNodeId = nodeId;
      return nodeId;
    },

    resetSelection() {
      this.selectedProcessId = '';
      this.selectedProcess = null;
      this.outputItems = [];
      this.stopPolling();
    },

    async loadNodes() {
      this.loadingNodes = true;
      this.setError('');
      try {
        const response = await fetch(buildApiPath('/api/v2/nodes'));
        if (!response.ok) {
          throw new Error(await parseErrorMessage(response, `load nodes failed: ${response.status}`));
        }
        const payload = await response.json();
        this.nodes = Array.isArray(payload?.items) ? payload.items : [];
        this.ensureSelectedNode();
        return this.nodes;
      } catch (error) {
        this.setError(String(error?.message || error || 'load nodes failed'));
        throw error;
      } finally {
        this.loadingNodes = false;
      }
    },

    async loadProcesses(nodeId = this.selectedNodeId) {
      const targetNodeId = this.resolvePreferredNodeId(nodeId);
      this.selectedNodeId = targetNodeId;
      if (!targetNodeId) {
        this.items = [];
        this.resetSelection();
        return [];
      }

      this.loadingList = true;
      this.setError('');
      try {
        const response = await fetch(buildNodeProcessesPath(targetNodeId));
        if (!response.ok) {
          throw new Error(await parseErrorMessage(response, `load processes failed: ${response.status}`));
        }
        const payload = await response.json();
        this.items = Array.isArray(payload?.items) ? payload.items : [];

        if (!this.selectedProcessId && this.items.length > 0) {
          this.selectedProcessId = String(this.items[0].processId || '');
        }
      } catch (error) {
        this.setError(String(error?.message || error || 'load processes failed'));
      } finally {
        this.loadingList = false;
      }
    },

    async loadProcessDetails(processId = this.selectedProcessId, nodeId = this.selectedNodeId) {
      const id = String(processId || '').trim();
      const targetNodeId = this.resolvePreferredNodeId(nodeId);
      if (!id) {
        this.selectedProcess = null;
        return null;
      }

      this.loadingDetails = true;
      try {
        const response = await fetch(buildNodeProcessesPath(targetNodeId, `/${encodeURIComponent(id)}`));
        if (!response.ok) {
          throw new Error(await parseErrorMessage(response, `load process failed: ${response.status}`));
        }
        const payload = await response.json();
        if (this.selectedProcessId === id) {
          this.selectedProcess = payload;
        }

        const index = this.items.findIndex((item) => item.processId === id);
        if (index >= 0) {
          this.items.splice(index, 1, payload);
        } else {
          this.items = [payload, ...this.items];
        }
        return payload;
      } catch (error) {
        this.setError(String(error?.message || error || 'load process failed'));
        throw error;
      } finally {
        this.loadingDetails = false;
      }
    },

    async loadOutput(processId = this.selectedProcessId, nodeId = this.selectedNodeId) {
      const id = String(processId || '').trim();
      const targetNodeId = this.resolvePreferredNodeId(nodeId);
      if (!id) {
        this.outputItems = [];
        return [];
      }

      this.loadingOutput = true;
      try {
        const response = await fetch(buildNodeProcessesPath(targetNodeId, `/${encodeURIComponent(id)}/output`));
        if (!response.ok) {
          throw new Error(await parseErrorMessage(response, `load output failed: ${response.status}`));
        }
        const payload = await response.json();
        const items = Array.isArray(payload?.items) ? payload.items : [];
        if (this.selectedProcessId === id) {
          this.outputItems = items;
        }
        return items;
      } catch (error) {
        this.setError(String(error?.message || error || 'load output failed'));
        throw error;
      } finally {
        this.loadingOutput = false;
      }
    },

    async initialize(preferredNodeId = '') {
      await this.loadNodes();
      const nodeId = this.resolvePreferredNodeId(preferredNodeId || this.selectedNodeId);
      if (!nodeId) {
        this.items = [];
        this.resetSelection();
        return;
      }
      this.selectedNodeId = nodeId;
      await this.loadProcesses(nodeId);
      if (this.selectedProcessId) {
        await this.selectProcess(this.selectedProcessId);
      }
    },

    async setSelectedNode(nodeId) {
      const normalizedNodeId = this.resolvePreferredNodeId(nodeId);
      if (!normalizedNodeId) {
        this.selectedNodeId = '';
        this.items = [];
        this.resetSelection();
        return;
      }
      if (normalizedNodeId === this.selectedNodeId && this.items.length > 0) {
        return;
      }

      this.selectedNodeId = normalizedNodeId;
      this.items = [];
      this.resetSelection();
      await this.loadProcesses(normalizedNodeId);
      if (this.selectedProcessId) {
        await this.selectProcess(this.selectedProcessId);
      }
    },

    async refreshSelected() {
      const nodeId = this.ensureSelectedNode();
      const id = String(this.selectedProcessId || '').trim();
      if (!nodeId) {
        return;
      }

      if (!id) {
        await this.loadProcesses(nodeId);
        return;
      }

      await Promise.all([
        this.loadProcesses(nodeId),
        this.loadProcessDetails(id, nodeId),
        this.loadOutput(id, nodeId)
      ]);
      this.syncPolling();
    },

    async selectProcess(processId) {
      const id = String(processId || '').trim();
      const nodeId = this.ensureSelectedNode();
      this.selectedProcessId = id;
      this.setError('');
      if (!id) {
        this.selectedProcess = null;
        this.outputItems = [];
        this.stopPolling();
        return;
      }

      await Promise.all([
        this.loadProcessDetails(id, nodeId),
        this.loadOutput(id, nodeId)
      ]);
      this.syncPolling();
    },

    async createProcess() {
      const nodeId = this.ensureSelectedNode();
      if (!nodeId) {
        throw new Error('请先选择节点');
      }
      if (!this.isSelectedNodeOnline) {
        throw new Error('当前节点离线，无法创建进程');
      }

      this.submitting = true;
      this.setError('');
      try {
        const response = await fetch(buildNodeProcessesPath(nodeId), {
          method: 'POST',
          headers: { 'content-type': 'application/json' },
          body: JSON.stringify(this.buildRunRequest())
        });
        if (!response.ok) {
          throw new Error(await parseErrorMessage(response, `start process failed: ${response.status}`));
        }
        const payload = await response.json();
        const processId = String(payload?.processId || '');
        await this.loadProcesses(nodeId);
        await this.selectProcess(processId);
        this.setStatus(`已在节点 ${this.selectedNode?.node_name || nodeId} 启动进程：${processId}`);
        return payload;
      } catch (error) {
        this.setError(String(error?.message || error || 'start process failed'));
        throw error;
      } finally {
        this.submitting = false;
      }
    },

    async waitForSelected(timeoutMs = null) {
      const id = String(this.selectedProcessId || '').trim();
      const nodeId = this.ensureSelectedNode();
      if (!id) {
        return null;
      }
      if (!this.isSelectedNodeOnline) {
        throw new Error('当前节点离线，无法等待进程');
      }

      this.waiting = true;
      this.setError('');
      try {
        const response = await fetch(buildNodeProcessesPath(nodeId, `/${encodeURIComponent(id)}/wait`, timeoutMs ? { timeout_ms: timeoutMs } : undefined), {
          method: 'POST'
        });
        if (!response.ok) {
          throw new Error(await parseErrorMessage(response, `wait failed: ${response.status}`));
        }
        const payload = await response.json();
        await this.refreshSelected();
        this.setStatus(payload?.completed ? `进程已完成：${id}` : `等待结束，进程仍在运行：${id}`);
        return payload;
      } catch (error) {
        this.setError(String(error?.message || error || 'wait failed'));
        throw error;
      } finally {
        this.waiting = false;
      }
    },

    async stopSelected(force = false) {
      const id = String(this.selectedProcessId || '').trim();
      const nodeId = this.ensureSelectedNode();
      if (!id) {
        return null;
      }
      if (!this.isSelectedNodeOnline) {
        throw new Error('当前节点离线，无法停止进程');
      }

      this.stopping = true;
      this.setError('');
      try {
        const response = await fetch(buildNodeProcessesPath(nodeId, `/${encodeURIComponent(id)}/stop`), {
          method: 'POST',
          headers: { 'content-type': 'application/json' },
          body: JSON.stringify({ force })
        });
        if (!response.ok) {
          throw new Error(await parseErrorMessage(response, `stop failed: ${response.status}`));
        }
        const payload = await response.json();
        await this.refreshSelected();
        this.setStatus(`已停止进程：${id}`);
        return payload;
      } catch (error) {
        this.setError(String(error?.message || error || 'stop failed'));
        throw error;
      } finally {
        this.stopping = false;
      }
    },

    async removeProcess(processId = this.selectedProcessId) {
      const id = String(processId || '').trim();
      const nodeId = this.ensureSelectedNode();
      if (!id) {
        return null;
      }
      if (!this.isSelectedNodeOnline) {
        throw new Error('当前节点离线，无法删除进程');
      }

      this.removing = true;
      this.removingProcessId = id;
      this.setError('');
      try {
        const response = await fetch(buildNodeProcessesPath(nodeId, `/${encodeURIComponent(id)}`), {
          method: 'DELETE'
        });
        if (!response.ok) {
          throw new Error(await parseErrorMessage(response, `remove failed: ${response.status}`));
        }
        const payload = await response.json();
        const nextItems = this.items.filter((item) => item.processId !== id);
        this.items = nextItems;

        if (this.selectedProcessId === id) {
          this.selectedProcess = null;
          this.outputItems = [];
          this.stopPolling();
          this.selectedProcessId = String(nextItems[0]?.processId || '');
          if (this.selectedProcessId) {
            await this.selectProcess(this.selectedProcessId);
          }
        }

        this.setStatus(`已删除进程：${id}`);
        return payload;
      } catch (error) {
        this.setError(String(error?.message || error || 'remove failed'));
        throw error;
      } finally {
        this.removing = false;
        this.removingProcessId = '';
      }
    },

    async removeSelected() {
      return this.removeProcess(this.selectedProcessId);
    },

    isSelectedRunning() {
      return ['pending', 'running'].includes(this.selectedProcessStatus);
    },

    stopPolling() {
      if (this.pollTimer) {
        clearInterval(this.pollTimer);
        this.pollTimer = null;
      }
    },

    syncPolling() {
      if (!this.selectedProcessId || !this.isSelectedRunning()) {
        this.stopPolling();
        return;
      }

      if (this.pollTimer) {
        return;
      }

      this.pollTimer = setInterval(() => {
        this.refreshSelected().catch(() => {});
      }, 1500);
    },

    dispose() {
      this.stopPolling();
    }
  }
});
