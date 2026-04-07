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

function normalizeArgs(value) {
  return Array.isArray(value)
    ? value.map((item) => String(item ?? '')).filter(Boolean)
    : [];
}

function normalizeEnv(value) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    return {};
  }

  const result = {};
  for (const [key, raw] of Object.entries(value)) {
    const envKey = String(key || '').trim();
    if (!envKey) {
      continue;
    }
    result[envKey] = String(raw ?? '');
  }
  return result;
}

function normalizeStrings(value) {
  return Array.isArray(value)
    ? value.map((item) => String(item ?? '').trim()).filter(Boolean)
    : [];
}

function normalizeRecipe(item) {
  return {
    id: String(item?.template_id || item?.templateId || item?.id || '').trim(),
    name: String(item?.name || '').trim(),
    group: String(item?.group || item?.description || 'general').trim() || 'general',
    cwd: String(item?.default_cwd || item?.defaultCwd || item?.cwd || '').trim(),
    command: String(item?.executable || item?.command || '').trim(),
    args: normalizeArgs(item?.base_args || item?.baseArgs || item?.args),
    env: normalizeEnv(item?.default_env || item?.defaultEnv || item?.env),
    envOverrides: normalizeEnv(item?.default_env || item?.defaultEnv || item?.envOverrides || item?.env),
    envEntryIds: normalizeStrings(item?.env_entry_ids || item?.envEntryIds),
    envGroupNames: normalizeStrings(item?.env_group_names || item?.envGroupNames),
    supportedOs: normalizeStrings(item?.supported_os || item?.supportedOs),
    createdAt: String(item?.created_at || item?.createdAt || '').trim(),
    updatedAt: String(item?.updated_at || item?.updatedAt || '').trim(),
    isDefault: item?.is_default === true || item?.isDefault === true,
    templateKind: String(item?.template_kind || item?.templateKind || 'terminal').trim() || 'terminal'
  };
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

export const useWebCliRecipesStore = defineStore('webcliRecipes', {
  state: () => ({
    currentNodeId: '',
    loading: false,
    error: '',
    items: []
  }),

  getters: {
    defaultRecipe(state) {
      return state.items.find((item) => item.isDefault) || null;
    }
  },

  actions: {
    async fetchRecipes(nodeId) {
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
        const response = await fetch(buildNodeApiPath(targetNodeId, '/cli/templates', { kind: 'terminal' }));
        if (!response.ok) {
          throw new Error(await parseErrorMessage(response, `load recipes failed: ${response.status}`));
        }
        const payload = await response.json();
        this.items = Array.isArray(payload?.items)
          ? payload.items.map(normalizeRecipe).filter((item) => item.id)
          : [];
        return this.items;
      } catch (error) {
        this.items = [];
        this.error = String(error?.message || error || 'load recipes failed');
        throw error;
      } finally {
        this.loading = false;
      }
    },

    async addRecipe(input, nodeId = this.currentNodeId) {
      const targetNodeId = String(nodeId || '').trim();
      const payload = {
        template_id: String(input?.id || '').trim() || undefined,
        name: String(input?.name || '').trim(),
        template_kind: 'terminal',
        cli_type: 'custom',
        executable: String(input?.command || '').trim(),
        base_args: normalizeArgs(input?.args),
        default_cwd: String(input?.cwd || '').trim(),
        default_env: normalizeEnv(input?.env),
        env_entry_ids: normalizeStrings(input?.envEntryIds),
        env_group_names: normalizeStrings(input?.envGroupNames),
        supported_os: normalizeStrings(input?.supportedOs),
        description: String(input?.group || 'general').trim() || 'general',
        icon: 'terminal',
        color: '#0e639c',
        is_default: input?.isDefault === true
      };

      const response = await fetch(buildNodeApiPath(targetNodeId, '/cli/templates'), {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(payload)
      });
      if (!response.ok) {
        throw new Error(await parseErrorMessage(response, `create recipe failed: ${response.status}`));
      }

      const created = normalizeRecipe(await response.json());
      await this.fetchRecipes(targetNodeId);
      return created;
    },

    async updateRecipe(id, input, nodeId = this.currentNodeId) {
      const targetNodeId = String(nodeId || '').trim();
      const targetId = String(id || '').trim();
      const existing = this.items.find((item) => item.id === targetId);
      const payload = {
        name: String(input?.name ?? existing?.name ?? '').trim(),
        template_kind: 'terminal',
        cli_type: 'custom',
        executable: String(input?.command ?? existing?.command ?? '').trim(),
        base_args: normalizeArgs(input?.args ?? existing?.args),
        default_cwd: String(input?.cwd ?? existing?.cwd ?? '').trim(),
        default_env: normalizeEnv(input?.env ?? existing?.env),
        env_entry_ids: normalizeStrings(input?.envEntryIds ?? existing?.envEntryIds),
        env_group_names: normalizeStrings(input?.envGroupNames ?? existing?.envGroupNames),
        supported_os: normalizeStrings(input?.supportedOs ?? existing?.supportedOs),
        description: String(input?.group ?? existing?.group ?? 'general').trim() || 'general',
        icon: 'terminal',
        color: '#0e639c',
        is_default: input?.isDefault ?? existing?.isDefault ?? false
      };

      const response = await fetch(buildNodeApiPath(targetNodeId, `/cli/templates/${encodeURIComponent(targetId)}`), {
        method: 'PUT',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(payload)
      });
      if (!response.ok) {
        throw new Error(await parseErrorMessage(response, `update recipe failed: ${response.status}`));
      }

      const updated = normalizeRecipe(await response.json());
      await this.fetchRecipes(targetNodeId);
      return updated;
    },

    async removeRecipe(id, nodeId = this.currentNodeId) {
      const targetNodeId = String(nodeId || '').trim();
      const targetId = String(id || '').trim();
      const response = await fetch(buildNodeApiPath(targetNodeId, `/cli/templates/${encodeURIComponent(targetId)}`), {
        method: 'DELETE'
      });
      if (!response.ok) {
        throw new Error(await parseErrorMessage(response, `delete recipe failed: ${response.status}`));
      }
      await this.fetchRecipes(targetNodeId);
    },

    async setDefaultRecipe(id, nodeId = this.currentNodeId) {
      const targetNodeId = String(nodeId || '').trim();
      const targetId = String(id || '').trim();
      const current = this.items.find((item) => item.id === targetId);
      if (!current) {
        throw new Error('recipe not found');
      }
      await this.updateRecipe(targetId, { ...current, isDefault: true }, targetNodeId);
    },

    async clearDefaultRecipe(nodeId = this.currentNodeId) {
      const targetNodeId = String(nodeId || '').trim();
      const current = this.items.find((item) => item.isDefault);
      if (!current) {
        return;
      }
      await this.updateRecipe(current.id, { ...current, isDefault: false }, targetNodeId);
    }
  }
});
