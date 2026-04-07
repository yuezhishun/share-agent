import { defineStore } from 'pinia';
import { QUICK_COMMAND_INTERVAL_MS, normalizeShortcutGroup } from '../utils/desktop-terminal.js';

function resolveHttpBase() {
  return String(import.meta.env.VITE_WEBPTY_BASE || '').trim();
}

function buildApiPath(pathname) {
  const base = resolveHttpBase();
  const url = new URL(
    `${base}${pathname}`,
    typeof window !== 'undefined' ? window.location.origin : 'http://127.0.0.1'
  );
  return `${url.pathname}${url.search}`;
}

function normalizeShortcut(item) {
  const id = String(item?.shortcut_id || item?.shortcutId || item?.id || '').trim();
  const label = String(item?.label || '').trim();
  const command = String(item?.command || '').trim();
  const group = normalizeShortcutGroup(item?.group_name || item?.groupName || item?.group);
  const pressEnter = item?.press_enter !== false && item?.pressEnter !== false;
  const enabled = item?.enabled !== false;
  const createdAt = String(item?.created_at || item?.createdAt || '').trim();
  const updatedAt = String(item?.updated_at || item?.updatedAt || '').trim();

  if (!id || !label || !command) {
    return null;
  }

  return {
    id,
    label,
    command,
    group,
    pressEnter,
    enabled,
    createdAt,
    updatedAt,
    intervalMs: QUICK_COMMAND_INTERVAL_MS,
    sequence: pressEnter ? [command, '\r'] : [command],
    isCustom: true
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

export const useWebCliShortcutsStore = defineStore('webcliShortcuts', {
  state: () => ({
    loading: false,
    error: '',
    items: []
  }),

  actions: {
    async fetchShortcuts() {
      this.loading = true;
      this.error = '';
      try {
        const response = await fetch(buildApiPath('/api/terminal/shortcuts'));
        if (!response.ok) {
          throw new Error(await parseErrorMessage(response, `load shortcuts failed: ${response.status}`));
        }
        const payload = await response.json();
        this.items = Array.isArray(payload?.items)
          ? payload.items.map(normalizeShortcut).filter(Boolean)
          : [];
        return this.items;
      } catch (error) {
        this.items = [];
        this.error = String(error?.message || error || 'load shortcuts failed');
        throw error;
      } finally {
        this.loading = false;
      }
    },

    async addShortcut(input) {
      const payload = {
        label: String(input?.label || '').trim(),
        command: String(input?.command || '').trim(),
        groupName: normalizeShortcutGroup(input?.group),
        pressEnter: input?.pressEnter !== false,
        enabled: input?.enabled !== false
      };
      const response = await fetch(buildApiPath('/api/terminal/shortcuts'), {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(payload)
      });
      if (!response.ok) {
        throw new Error(await parseErrorMessage(response, `create shortcut failed: ${response.status}`));
      }

      const created = normalizeShortcut(await response.json());
      await this.fetchShortcuts();
      return created;
    },

    async removeShortcut(id) {
      const targetId = String(id || '').trim();
      const response = await fetch(buildApiPath(`/api/terminal/shortcuts/${encodeURIComponent(targetId)}`), {
        method: 'DELETE'
      });
      if (!response.ok) {
        throw new Error(await parseErrorMessage(response, `delete shortcut failed: ${response.status}`));
      }

      await this.fetchShortcuts();
    }
  }
});
