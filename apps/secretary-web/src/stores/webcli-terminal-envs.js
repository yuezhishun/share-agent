import { defineStore } from 'pinia';

function resolveHttpBase() {
  return String(import.meta.env.VITE_WEBPTY_BASE || '').trim();
}

function buildNodeApiPath(nodeId, pathname, params) {
  const base = resolveHttpBase();
  const normalizedNodeId = String(nodeId || '').trim();
  const url = new URL(
    `${base}/api/nodes/${encodeURIComponent(normalizedNodeId)}${pathname}`,
    typeof window !== 'undefined' ? window.location.origin : 'http://127.0.0.1'
  );
  for (const [key, value] of Object.entries(params || {})) {
    if (value === undefined || value === null || value === '') {
      continue;
    }
    url.searchParams.set(key, String(value));
  }
  return `${url.pathname}${url.search}`;
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

function normalizeValue(raw, valueType) {
  if (valueType === 'array') {
    return Array.isArray(raw)
      ? raw.map((item) => String(item ?? '').trim()).filter(Boolean)
      : [];
  }
  return String(raw ?? '');
}

function normalizeEntry(item) {
  const valueType = String(item?.valueType || item?.value_type || (Array.isArray(item?.value) ? 'array' : 'string')).trim() === 'array'
    ? 'array'
    : 'string';
  return {
    id: String(item?.id || item?.env_id || item?.envId || '').trim(),
    key: String(item?.key || '').trim(),
    valueType,
    value: normalizeValue(item?.value, valueType),
    group: String(item?.group || item?.group_name || item?.groupName || 'general').trim() || 'general',
    enabled: item?.enabled !== false,
    sortOrder: Number(item?.sortOrder ?? item?.sort_order ?? 0) || 0,
    createdAt: String(item?.createdAt || item?.created_at || '').trim(),
    updatedAt: String(item?.updatedAt || item?.updated_at || '').trim()
  };
}

function toPayload(input) {
  const value = input?.value;
  const valueType = String(input?.valueType || (Array.isArray(value) ? 'array' : 'string')).trim() === 'array'
    ? 'array'
    : 'string';
  return {
    key: String(input?.key || '').trim(),
    value: valueType === 'array'
      ? (Array.isArray(value) ? value.map((item) => String(item ?? '').trim()).filter(Boolean) : [])
      : String(value ?? ''),
    group_name: String(input?.group || 'general').trim() || 'general',
    sort_order: Number(input?.sortOrder || 0) || 0,
    enabled: input?.enabled !== false
  };
}

export const useWebCliTerminalEnvsStore = defineStore('webcliTerminalEnvs', {
  state: () => ({
    currentNodeId: '',
    loading: false,
    saving: false,
    error: '',
    items: []
  }),

  getters: {
    groups(state) {
      return Array.from(new Set(state.items.map((item) => item.group).filter(Boolean))).sort((a, b) => a.localeCompare(b, 'zh-Hans-CN'));
    }
  },

  actions: {
    async fetchEntries(nodeId, params = {}) {
      const targetNodeId = String(nodeId || '').trim();
      if (!targetNodeId) {
        this.currentNodeId = '';
        this.items = [];
        this.error = '';
        return [];
      }

      this.currentNodeId = targetNodeId;
      this.loading = true;
      this.error = '';
      try {
        const response = await fetch(buildNodeApiPath(targetNodeId, '/terminal-envs', params));
        if (!response.ok) {
          throw new Error(await parseErrorMessage(response, `load terminal envs failed: ${response.status}`));
        }
        const payload = await response.json();
        this.items = Array.isArray(payload?.items)
          ? payload.items.map(normalizeEntry).filter((item) => item.id)
          : [];
        return this.items;
      } catch (error) {
        this.items = [];
        this.error = String(error?.message || error || 'load terminal envs failed');
        throw error;
      } finally {
        this.loading = false;
      }
    },

    async addEntry(input, nodeId = this.currentNodeId) {
      const targetNodeId = String(nodeId || '').trim();
      this.saving = true;
      try {
        const response = await fetch(buildNodeApiPath(targetNodeId, '/terminal-envs'), {
          method: 'POST',
          headers: { 'content-type': 'application/json' },
          body: JSON.stringify(toPayload(input))
        });
        if (!response.ok) {
          throw new Error(await parseErrorMessage(response, `create terminal env failed: ${response.status}`));
        }
        const created = normalizeEntry(await response.json());
        await this.fetchEntries(targetNodeId);
        return created;
      } finally {
        this.saving = false;
      }
    },

    async updateEntry(id, input, nodeId = this.currentNodeId) {
      const targetNodeId = String(nodeId || '').trim();
      const targetId = String(id || '').trim();
      this.saving = true;
      try {
        const response = await fetch(buildNodeApiPath(targetNodeId, `/terminal-envs/${encodeURIComponent(targetId)}`), {
          method: 'PUT',
          headers: { 'content-type': 'application/json' },
          body: JSON.stringify(toPayload(input))
        });
        if (!response.ok) {
          throw new Error(await parseErrorMessage(response, `update terminal env failed: ${response.status}`));
        }
        const updated = normalizeEntry(await response.json());
        await this.fetchEntries(targetNodeId);
        return updated;
      } finally {
        this.saving = false;
      }
    },

    async removeEntry(id, nodeId = this.currentNodeId) {
      const targetNodeId = String(nodeId || '').trim();
      const targetId = String(id || '').trim();
      this.saving = true;
      try {
        const response = await fetch(buildNodeApiPath(targetNodeId, `/terminal-envs/${encodeURIComponent(targetId)}`), {
          method: 'DELETE'
        });
        if (!response.ok) {
          throw new Error(await parseErrorMessage(response, `delete terminal env failed: ${response.status}`));
        }
        await this.fetchEntries(targetNodeId);
      } finally {
        this.saving = false;
      }
    }
  }
});
