import { computed, ref } from 'vue';
import { FILE_CHUNK_BYTES, FILE_CHUNK_MAX_LINES, resolveEditorKind } from '../utils/desktop-terminal.js';

export function useDesktopTerminalFileTabs({
  buildNodeFileApiPath,
  parseErrorMessage,
  filesStore,
  getActiveNodeId,
  switchCenterTab,
  activeCenterTab,
  setStatus
}) {
  const fileTabs = ref([]);
  const activeFileTab = computed(() => {
    if (activeCenterTab.value === 'terminal') {
      return null;
    }
    return fileTabs.value.find((tab) => tab.id === activeCenterTab.value) || null;
  });

  async function openFileDocument(path, nodeId = getActiveNodeId(), options = {}) {
    const response = await fetch(buildNodeFileApiPath(nodeId, '/read', {
      path: String(path || ''),
      mode: String(options.mode || 'edit'),
      max_lines: Number(options.maxLines) || FILE_CHUNK_MAX_LINES,
      chunk_bytes: Number(options.chunkBytes) || FILE_CHUNK_BYTES,
      line_offset: Math.max(0, Number(options.lineOffset) || 0),
      direction: options.direction ? String(options.direction) : undefined
    }));
    if (!response.ok) {
      throw new Error(await parseErrorMessage(response, `read failed: ${response.status}`));
    }
    return response.json();
  }

  function buildEmptyFileTab(tabId, normalizedNodeId, normalizedPath, displayName) {
    const editorKind = resolveEditorKind(normalizedPath);
    return {
      id: tabId,
      nodeId: normalizedNodeId,
      path: normalizedPath,
      name: displayName || normalizedPath.split('/').pop() || normalizedPath,
      size: 0,
      editorKind,
      loading: true,
      error: '',
      content: '',
      previewUrl: '',
      zoom: 1,
      lastSavedContent: '',
      dirty: false,
      truncated: false,
      truncateReason: '',
      readOnly: false,
      largeFile: false,
      hasMoreBefore: false,
      hasMoreAfter: false,
      cursorStart: 0,
      cursorEnd: 0,
      loadedBytes: 0,
      loadedLines: 0,
      mode: 'edit'
    };
  }

  function mapFileReadPayload(payload, existingContent = '') {
    const nextContent = String(payload?.content || '');
    const readOnly = payload?.read_only === true;
    const mode = String(payload?.mode || 'edit');
    const appendContent = readOnly && mode === 'progressive' && Number(payload?.cursor_start || 0) > 0 && existingContent;
    return {
      content: appendContent ? `${existingContent}\n${nextContent}` : nextContent,
      lastSavedContent: appendContent ? `${existingContent}\n${nextContent}` : nextContent,
      dirty: false,
      error: '',
      size: Math.max(0, Number(payload?.size) || 0),
      truncated: payload?.truncated === true,
      truncateReason: String(payload?.truncate_reason || ''),
      readOnly,
      largeFile: payload?.large_file === true,
      hasMoreBefore: payload?.has_more_before === true,
      hasMoreAfter: payload?.has_more_after === true,
      cursorStart: Math.max(0, Number(payload?.cursor_start) || 0),
      cursorEnd: Math.max(0, Number(payload?.cursor_end) || 0),
      loadedBytes: Math.max(0, Number(payload?.loaded_bytes) || 0),
      loadedLines: Math.max(0, Number(payload?.loaded_lines || payload?.lines_shown) || 0),
      mode
    };
  }

  function findFileTabIndex(tabId) {
    return fileTabs.value.findIndex((item) => item.id === tabId);
  }

  function patchFileTab(tabId, patch) {
    const index = findFileTabIndex(tabId);
    if (index < 0) {
      return null;
    }
    const current = fileTabs.value[index];
    const next = {
      ...current,
      ...patch
    };
    fileTabs.value.splice(index, 1, next);
    return next;
  }

  async function openFileTab(path, displayName = '', nodeId = getActiveNodeId()) {
    const normalizedPath = String(path || '').trim();
    const normalizedNodeId = String(nodeId || '').trim();
    if (!normalizedPath) {
      return;
    }

    const tabId = `file:${normalizedNodeId}:${normalizedPath}`;
    const existing = fileTabs.value.find((x) => x.id === tabId);
    if (existing) {
      switchCenterTab(tabId);
      return;
    }

    const tab = buildEmptyFileTab(tabId, normalizedNodeId, normalizedPath, displayName);
    fileTabs.value = [...fileTabs.value, tab];
    switchCenterTab(tabId);

    if (tab.editorKind === 'image') {
      patchFileTab(tabId, {
        size: Number.isFinite(Number(tab.size)) ? tab.size : 0,
        previewUrl: buildNodeFileApiPath(normalizedNodeId, '/download', {
          path: normalizedPath
        }),
        loading: false,
        readOnly: true,
        dirty: false,
        truncated: false,
        largeFile: false,
        mode: 'preview'
      });
      return;
    }

    try {
      const payload = await openFileDocument(normalizedPath, normalizedNodeId);
      patchFileTab(tabId, mapFileReadPayload(payload));
    } catch (error) {
      patchFileTab(tabId, {
        error: String(error?.message || error || 'open file failed')
      });
    } finally {
      patchFileTab(tabId, {
        loading: false
      });
    }
  }

  async function openFileEntry(item) {
    if (!item?.path) {
      return;
    }

    if (item.kind === 'dir') {
      await filesStore.loadList(item.path, getActiveNodeId());
      return;
    }

    await openFileTab(item.path, item.name, getActiveNodeId());
  }

  function closeFileTab(tabId) {
    const id = String(tabId || '').trim();
    if (!id) {
      return;
    }
    const current = fileTabs.value.find((item) => item.id === id);
    if (current?.dirty && !window.confirm('当前文件有未保存修改，确认关闭并丢弃修改？')) {
      return;
    }
    const next = fileTabs.value.filter((x) => x.id !== id);
    fileTabs.value = next;
    if (activeCenterTab.value === id) {
      switchCenterTab('terminal');
    }
  }

  function closeAllFileTabs() {
    if (fileTabs.value.length === 0) {
      return;
    }

    const dirtyTabs = fileTabs.value.filter((tab) => tab?.dirty);
    if (
      dirtyTabs.length > 0
      && !window.confirm(`当前有 ${dirtyTabs.length} 个文件未保存，确认全部关闭并丢弃修改？`)
    ) {
      return;
    }

    fileTabs.value = [];
    switchCenterTab('terminal');
  }

  function updateActiveFileContent(value) {
    const tab = activeFileTab.value;
    if (!tab || tab.readOnly || tab.editorKind === 'image') {
      return;
    }
    patchFileTab(tab.id, {
      content: String(value ?? ''),
      dirty: String(value ?? '') !== tab.lastSavedContent
    });
  }

  function updateActiveImageZoom(direction) {
    const tab = activeFileTab.value;
    if (!tab || tab.editorKind !== 'image') {
      return;
    }
    const current = Number(tab.zoom) || 1;
    const next = direction === 'reset'
      ? 1
      : Math.min(4, Math.max(0.25, Number((current + (direction === 'in' ? 0.25 : -0.25)).toFixed(2))));
    patchFileTab(tab.id, { zoom: next });
  }

  async function saveActiveFileTab() {
    const tab = activeFileTab.value;
    if (!tab || tab.loading) {
      return;
    }
    if (tab.readOnly) {
      setStatus('大文件只读预览模式不支持保存');
      return;
    }

    patchFileTab(tab.id, { error: '' });
    try {
      await filesStore.saveFile(tab.path, tab.content, tab.nodeId);
      patchFileTab(tab.id, {
        lastSavedContent: tab.content,
        dirty: false
      });
      setStatus(`Saved: ${tab.path}`);
    } catch (error) {
      patchFileTab(tab.id, {
        error: String(error?.message || error || 'save failed')
      });
    }
  }

  async function reloadFileTab(tab) {
    if (!tab || tab.loading) {
      return;
    }
    if (tab.readOnly) {
      patchFileTab(tab.id, {
        loading: true,
        error: ''
      });
      try {
        const payload = await openFileDocument(tab.path, tab.nodeId);
        patchFileTab(tab.id, mapFileReadPayload(payload));
      } catch (error) {
        patchFileTab(tab.id, {
          error: String(error?.message || error || 'reload failed')
        });
      } finally {
        patchFileTab(tab.id, {
          loading: false
        });
      }
      return;
    }
    if (tab.dirty && !window.confirm('当前文件有未保存修改，确认重载并丢弃修改？')) {
      return;
    }

    patchFileTab(tab.id, {
      loading: true,
      error: ''
    });
    try {
      const payload = await openFileDocument(tab.path, tab.nodeId);
      patchFileTab(tab.id, mapFileReadPayload(payload));
    } catch (error) {
      patchFileTab(tab.id, {
        error: String(error?.message || error || 'reload failed')
      });
    } finally {
      patchFileTab(tab.id, {
        loading: false
      });
    }
  }

  async function loadMoreFileTab(tab) {
    if (!tab || tab.loading || tab.hasMoreAfter !== true) {
      return;
    }

    patchFileTab(tab.id, { loading: true, error: '' });
    try {
      const payload = await openFileDocument(tab.path, tab.nodeId, {
        mode: 'progressive',
        lineOffset: tab.cursorEnd,
        direction: 'forward'
      });
      patchFileTab(tab.id, mapFileReadPayload(payload, tab.content));
    } catch (error) {
      patchFileTab(tab.id, {
        error: String(error?.message || error || 'load more failed')
      });
    } finally {
      patchFileTab(tab.id, { loading: false });
    }
  }

  async function previewFileTabTail(tab) {
    if (!tab || tab.loading) {
      return;
    }

    patchFileTab(tab.id, { loading: true, error: '' });
    try {
      const payload = await openFileDocument(tab.path, tab.nodeId, {
        mode: 'progressive',
        direction: 'tail'
      });
      patchFileTab(tab.id, mapFileReadPayload(payload));
    } catch (error) {
      patchFileTab(tab.id, {
        error: String(error?.message || error || 'tail preview failed')
      });
    } finally {
      patchFileTab(tab.id, { loading: false });
    }
  }

  async function loadFileTabFromStart(tab) {
    if (!tab || tab.loading) {
      return;
    }

    patchFileTab(tab.id, { loading: true, error: '' });
    try {
      const payload = await openFileDocument(tab.path, tab.nodeId, {
        mode: 'edit'
      });
      patchFileTab(tab.id, mapFileReadPayload(payload));
    } catch (error) {
      patchFileTab(tab.id, {
        error: String(error?.message || error || 'load from start failed')
      });
    } finally {
      patchFileTab(tab.id, { loading: false });
    }
  }

  return {
    fileTabs,
    activeFileTab,
    openFileEntry,
    closeFileTab,
    closeAllFileTabs,
    updateActiveFileContent,
    updateActiveImageZoom,
    saveActiveFileTab,
    reloadFileTab,
    loadMoreFileTab,
    previewFileTabTail,
    loadFileTabFromStart
  };
}
