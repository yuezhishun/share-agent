import { defineStore } from 'pinia';

function resolveHttpBase() {
  return String(import.meta.env.VITE_WEBPTY_BASE || '').trim();
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

function buildNodeFilesApiPath(pathname, nodeId, params) {
  const normalizedNodeId = String(nodeId || '').trim();
  if (!normalizedNodeId) {
    return buildApiPath(pathname, params);
  }
  return buildApiPath(`/api/nodes/${encodeURIComponent(normalizedNodeId)}${pathname}`, params);
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

function triggerDownload(blob, fileName) {
  if (typeof window === 'undefined' || typeof document === 'undefined') {
    return;
  }
  const targetName = String(fileName || 'download.bin');
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = targetName;
  anchor.style.display = 'none';
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  URL.revokeObjectURL(url);
}

function parseDownloadName(headerValue) {
  const raw = String(headerValue || '');
  if (!raw) {
    return '';
  }
  const utf8Match = raw.match(/filename\*=UTF-8''([^;]+)/i);
  if (utf8Match?.[1]) {
    try {
      return decodeURIComponent(utf8Match[1]);
    } catch {
      return utf8Match[1];
    }
  }

  const basicMatch = raw.match(/filename=\"?([^\";]+)\"?/i);
  return basicMatch?.[1] ? basicMatch[1] : '';
}

export const useWebCliFilesStore = defineStore('webcliFiles', {
  state: () => ({
    currentNodeId: '',
    basePath: '',
    currentPath: '',
    parentPath: '',
    showHidden: false,
    loading: false,
    error: '',
    items: [],
    preview: null,
    previewError: '',
    actionLoading: false,
    actionError: ''
  }),

  actions: {
    resetState(next = {}) {
      this.currentNodeId = String(next.currentNodeId || '').trim();
      this.basePath = String(next.basePath || '');
      this.currentPath = String(next.currentPath || '');
      this.parentPath = '';
      this.loading = false;
      this.error = '';
      this.items = [];
      this.preview = null;
      this.previewError = '';
      this.actionLoading = false;
      this.actionError = '';
    },

    resetPreview() {
      this.preview = null;
      this.previewError = '';
    },

    setActionError(value) {
      this.actionError = String(value || '');
    },

    setCurrentPath(path) {
      this.currentPath = String(path || this.basePath);
    },

    setCurrentNodeId(nodeId) {
      this.currentNodeId = String(nodeId || '').trim();
    },

    async loadList(path, nodeId = this.currentNodeId) {
      const targetNodeId = String(nodeId || '').trim();
      this.setCurrentNodeId(targetNodeId);
      this.loading = true;
      this.error = '';
      this.resetPreview();
      try {
        const requestedPath = path === undefined || path === null ? this.currentPath : path;
        const response = await fetch(buildNodeFilesApiPath('/files/list', targetNodeId, {
          path: requestedPath,
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

    async readFile(path, nodeId = this.currentNodeId) {
      const targetNodeId = String(nodeId || '').trim();
      this.setCurrentNodeId(targetNodeId);
      this.previewError = '';
      try {
        const response = await fetch(buildNodeFilesApiPath('/files/read', targetNodeId, {
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

    async saveFile(path, content, nodeId = this.currentNodeId) {
      const targetNodeId = String(nodeId || '').trim();
      this.setCurrentNodeId(targetNodeId);
      this.actionLoading = true;
      this.setActionError('');
      try {
        const response = await fetch(buildNodeFilesApiPath('/files/write', targetNodeId), {
          method: 'POST',
          headers: { 'content-type': 'application/json' },
          body: JSON.stringify({
            path,
            content: String(content ?? '')
          })
        });
        if (!response.ok) {
          throw new Error(await parseErrorMessage(response, `write failed: ${response.status}`));
        }

        const payload = await response.json();
        if (this.preview?.path === path) {
          this.preview = {
            ...this.preview,
            content: String(content ?? ''),
            size: Number(payload?.size || this.preview.size || 0),
            truncated: false,
            truncateReason: null
          };
        }
        return payload;
      } catch (error) {
        const message = String(error?.message || error);
        this.setActionError(message);
        throw error;
      } finally {
        this.actionLoading = false;
      }
    },

    async openEntry(item, nodeId = this.currentNodeId) {
      if (!item || !item.path) {
        return;
      }
      if (item.kind === 'dir') {
        await this.loadList(item.path, nodeId);
        return;
      }
      if (item.kind === 'file') {
        await this.readFile(item.path, nodeId);
      }
    },

    async createDirectory(name, parentPath, nodeId = this.currentNodeId) {
      const targetNodeId = String(nodeId || '').trim();
      this.setCurrentNodeId(targetNodeId);
      this.actionLoading = true;
      this.setActionError('');
      try {
        const targetPath = String(parentPath || this.currentPath || this.basePath);
        const response = await fetch(buildNodeFilesApiPath('/files/mkdir', targetNodeId), {
          method: 'POST',
          headers: { 'content-type': 'application/json' },
          body: JSON.stringify({
            path: targetPath,
            name
          })
        });
        if (!response.ok) {
          throw new Error(await parseErrorMessage(response, `mkdir failed: ${response.status}`));
        }

        const payload = await response.json();
        await this.loadList(targetPath, targetNodeId);
        return payload?.item || null;
      } catch (error) {
        const message = String(error?.message || error);
        this.setActionError(message);
        throw error;
      } finally {
        this.actionLoading = false;
      }
    },

    async renameEntry(path, newName, nodeId = this.currentNodeId) {
      const targetNodeId = String(nodeId || '').trim();
      this.setCurrentNodeId(targetNodeId);
      this.actionLoading = true;
      this.setActionError('');
      try {
        const response = await fetch(buildNodeFilesApiPath('/files/rename', targetNodeId), {
          method: 'POST',
          headers: { 'content-type': 'application/json' },
          body: JSON.stringify({
            path,
            new_name: newName
          })
        });
        if (!response.ok) {
          throw new Error(await parseErrorMessage(response, `rename failed: ${response.status}`));
        }

        const payload = await response.json();
        await this.loadList(this.currentPath, targetNodeId);
        return payload?.item || null;
      } catch (error) {
        const message = String(error?.message || error);
        this.setActionError(message);
        throw error;
      } finally {
        this.actionLoading = false;
      }
    },

    async removeEntry(path, options = {}, nodeId = this.currentNodeId) {
      const targetNodeId = String(nodeId || '').trim();
      this.setCurrentNodeId(targetNodeId);
      this.actionLoading = true;
      this.setActionError('');
      try {
        const recursive = options?.recursive ? '1' : '0';
        const response = await fetch(buildNodeFilesApiPath('/files/remove', targetNodeId, {
          path,
          recursive
        }), {
          method: 'DELETE'
        });
        if (!response.ok) {
          throw new Error(await parseErrorMessage(response, `remove failed: ${response.status}`));
        }

        const payload = await response.json();
        await this.loadList(this.currentPath, targetNodeId);
        return payload;
      } catch (error) {
        const message = String(error?.message || error);
        this.setActionError(message);
        throw error;
      } finally {
        this.actionLoading = false;
      }
    },

    async uploadFiles(fileList, targetPath, nodeId = this.currentNodeId, options = {}) {
      const files = Array.from(fileList || []).filter(Boolean);
      if (files.length === 0) {
        return [];
      }

      const targetNodeId = String(nodeId || '').trim();
      const instanceId = String(options?.instanceId || '').trim();
      this.setCurrentNodeId(targetNodeId);
      this.actionLoading = true;
      this.setActionError('');
      try {
        const path = String(targetPath || this.currentPath || this.basePath);
        const uploaded = [];
        for (const file of files) {
          const form = new FormData();
          form.append('file', file);
          if (targetNodeId) {
            if (instanceId) {
              form.append('instance_id', instanceId);
            } else {
              form.append('path', path);
            }
          } else {
            form.append('path', path);
          }
          const response = await fetch(buildNodeFilesApiPath('/files/upload', targetNodeId), {
            method: 'POST',
            body: form
          });
          if (!response.ok) {
            throw new Error(await parseErrorMessage(response, `upload failed: ${response.status}`));
          }
          const payload = await response.json();
          uploaded.push(payload?.upload || null);
        }
        await this.loadList(path, targetNodeId);
        return uploaded;
      } catch (error) {
        const message = String(error?.message || error);
        this.setActionError(message);
        throw error;
      } finally {
        this.actionLoading = false;
      }
    },

    async downloadEntry(path, nodeId = this.currentNodeId) {
      const targetNodeId = String(nodeId || '').trim();
      this.setCurrentNodeId(targetNodeId);
      this.actionLoading = true;
      this.setActionError('');
      try {
        const response = await fetch(buildNodeFilesApiPath('/files/download', targetNodeId, { path }));
        if (!response.ok) {
          throw new Error(await parseErrorMessage(response, `download failed: ${response.status}`));
        }
        const blob = await response.blob();
        const fileName = parseDownloadName(response.headers.get('content-disposition')) || path.split('/').pop() || 'download.bin';
        triggerDownload(blob, fileName);
        return { ok: true, fileName };
      } catch (error) {
        const message = String(error?.message || error);
        this.setActionError(message);
        throw error;
      } finally {
        this.actionLoading = false;
      }
    },

    async downloadFile(path, nodeId = this.currentNodeId) {
      return this.downloadEntry(path, nodeId);
    }
  }
});
