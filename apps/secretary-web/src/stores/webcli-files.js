import { defineStore } from 'pinia';

function resolveHttpBase() {
  return String(import.meta.env.VITE_WEBPTY_BASE || '/web-pty').trim();
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
  const relative = `${url.pathname}${url.search}`;
  return relative;
}

export const useWebCliFilesStore = defineStore('webcliFiles', {
  state: () => ({
    basePath: '/home/yueyuan',
    currentPath: '/home/yueyuan',
    parentPath: '',
    showHidden: false,
    loading: false,
    error: '',
    items: [],
    preview: null,
    previewError: ''
  }),

  actions: {
    resetPreview() {
      this.preview = null;
      this.previewError = '';
    },

    setCurrentPath(path) {
      this.currentPath = String(path || this.basePath);
    },

    async loadList(path) {
      this.loading = true;
      this.error = '';
      this.resetPreview();
      try {
        const response = await fetch(buildApiPath('/api/files/list', {
          path: path || this.currentPath,
          show_hidden: this.showHidden ? '1' : '0'
        }));
        if (!response.ok) {
          const message = await response.text();
          throw new Error(message || `list failed: ${response.status}`);
        }

        const payload = await response.json();
        this.basePath = String(payload?.base || this.basePath);
        this.currentPath = String(payload?.path || this.basePath);
        this.parentPath = String(payload?.parent || '');
        this.items = Array.isArray(payload?.items) ? payload.items : [];
      } catch (error) {
        this.error = String(error?.message || error);
        this.items = [];
      } finally {
        this.loading = false;
      }
    },

    async readFile(path) {
      this.previewError = '';
      try {
        const response = await fetch(buildApiPath('/api/files/read', {
          path,
          max_lines: 500
        }));
        if (!response.ok) {
          const message = await response.text();
          throw new Error(message || `read failed: ${response.status}`);
        }

        const payload = await response.json();
        this.preview = {
          path: payload.path,
          content: payload.content,
          size: payload.size,
          linesShown: payload.lines_shown,
          truncated: payload.truncated,
          truncateReason: payload.truncate_reason
        };
      } catch (error) {
        this.previewError = String(error?.message || error);
      }
    },

    async openEntry(item) {
      if (!item || !item.path) {
        return;
      }
      if (item.kind === 'dir') {
        await this.loadList(item.path);
        return;
      }
      if (item.kind === 'file') {
        await this.readFile(item.path);
      }
    }
  }
});
