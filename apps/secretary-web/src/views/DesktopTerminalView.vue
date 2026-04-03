<template>
  <div class="app">
    <div class="toolbar">
      <div class="logo"><i class="fa-solid fa-terminal" /> 多终端管理器 (实时)</div>
    </div>

    <div class="main">
      <TerminalSidebar
        class="sidebar-column left-sidebar"
        :nodes="terminalStore.nodes"
        :create-node-id="createNodeId"
        :selected-node="selectedNode"
        :is-selected-node-online="isSelectedNodeOnline"
        :visible-terminal-instances="visibleTerminalInstances"
        :selected-instance-id="terminalStore.selectedInstanceId"
        :renaming-instance-id="renamingInstanceId"
        :rename-instance-value="renameInstanceValue"
        :create-terminal-title="createTerminalTitle"
        :format-node-option="formatNodeOption"
        :format-instance-tooltip="formatInstanceTooltip"
        :format-instance-display-name="formatInstanceDisplayName"
        :get-instance-alias="getInstanceAlias"
        :set-rename-instance-input-ref="setRenameInstanceInputRef"
        @target-node-change="onTargetNodeChange"
        @refresh-nodes="refreshTerminals"
        @toggle-terminal-size-editor="toggleTerminalSizeEditor"
        @update:terminal-size-draft-cols="terminalSizeDraftCols = $event"
        @update:terminal-size-draft-rows="terminalSizeDraftRows = $event"
        @apply-terminal-size="applyTerminalSize"
        @cancel-terminal-size-editor="cancelTerminalSizeEditor"
        @create-instance="createInstance"
        @refresh-terminals="refreshTerminals"
        @connect="connect"
        @update:rename-instance-value="renameInstanceValue = $event"
        @save-rename-instance="saveRenameInstance"
        @cancel-rename-instance="cancelRenameInstance"
        @sync-terminal-item="syncTerminalItem"
        @view-plain-text-item="viewPlainTextItem"
        @begin-rename-instance="beginRenameInstance"
        @close-terminal="closeTerminal"
      />

      <div class="terminal-panel">
        <div class="terminal-panel-content">
          <div class="terminal-header">
            <div class="terminal-tabs">
              <button
                type="button"
                class="tab-btn"
                :class="{ active: activeCenterTab === 'terminal' }"
                @click="switchCenterTab('terminal')"
              >
                <i class="fa-regular fa-window-maximize" />
                <span id="currentTerminalName">{{ currentTerminalName }}</span>
              </button>

              <button
                v-if="plainTextVisible"
                type="button"
                class="tab-btn file-tab"
                :class="{ active: activeCenterTab === 'plain-text' }"
                @click="switchCenterTab('plain-text')"
                :title="plainTextTitle"
              >
                <i class="fa-regular fa-file-lines" />
                <span class="tab-text">{{ plainTextTitle }}</span>
                <i class="fa-solid fa-xmark close-icon" @click.stop="closePlainTextView" />
              </button>

              <button
                v-for="tab in fileTabs"
                :key="tab.id"
                type="button"
                class="tab-btn file-tab"
                :class="{ active: activeCenterTab === tab.id }"
                @click="switchCenterTab(tab.id)"
                :title="formatFileTabTooltip(tab)"
              >
                <i class="fa-regular fa-file" />
                <span class="tab-text">{{ tab.name }}</span>
                <i class="fa-solid fa-xmark close-icon" @click.stop="closeFileTab(tab.id)" />
              </button>

              <button
                v-if="showCloseAllFilesEntry"
                type="button"
                class="tab-btn tabs-action-btn"
                :title="closeAllFilesTitle"
                @click="closeAllFileTabs"
              >
                <i class="fa-regular fa-folder-open" />
                <span>关闭文件</span>
              </button>
            </div>
            <div class="status-text"><span data-testid="status"><i class="fa-regular fa-keyboard" /> {{ terminalStore.status }}</span></div>
          </div>

          <div v-show="activeCenterTab === 'terminal'" class="terminal-viewport">
            <div id="terminalContent" ref="terminalRef" class="terminal-host" data-testid="terminal" />
          </div>

          <div v-if="activeCenterTab === 'plain-text' && plainTextVisible" class="plain-text-panel">
            <div class="plain-text-toolbar">
              <span class="plain-text-toolbar-title">{{ plainTextTitle }}</span>
              <button type="button" @click="copyPlainText">复制纯文本</button>
            </div>
            <pre class="plain-text-content">{{ plainTextContent }}</pre>
          </div>

          <FileEditorPanel
            v-if="activeFileTab"
            :active-file-tab="activeFileTab"
            @reload="reloadFileTab(activeFileTab)"
            @save="saveActiveFileTab"
            @update:model-value="updateActiveFileContent"
            @load-more="loadMoreFileTab(activeFileTab)"
            @tail-preview="previewFileTabTail(activeFileTab)"
            @load-from-start="loadFileTabFromStart(activeFileTab)"
            @zoom-in="updateActiveImageZoom('in')"
            @zoom-out="updateActiveImageZoom('out')"
            @reset-zoom="updateActiveImageZoom('reset')"
          />
        </div>
      </div>

      <RightWorkspaceSidebar
        class="sidebar-column right-sidebar"
        :files-store="filesStore"
        :active-right-tab="activeRightTab"
        :right-tab-icon-class="rightTabIconClass"
        :right-tab-title="rightTabTitle"
        :show-folder-creator="showFolderCreator"
        :current-path-display="currentPathDisplay"
        :folder-name="folderName"
        :format-file-entry-tooltip="formatFileEntryTooltip"
        :format-size="formatSize"
        :show-shortcut-editor="showShortcutEditor"
        :shortcut-editor="shortcutEditor"
        :shortcut-groups="shortcutGroups"
        :voice-mode-enabled="voiceModeEnabled"
        :voice-mode-shortcut-label="voiceModeShortcutLabel"
        :show-terminal-size-editor="showTerminalSizeEditor"
        :terminal-size-draft-cols="terminalSizeDraftCols"
        :terminal-size-draft-rows="terminalSizeDraftRows"
        :show-recipe-editor="showRecipeEditor"
        :recipe-editor="recipeEditor"
        :recipe-folders-loading="recipeFoldersLoading"
        :recipe-folder-options="recipeFolderOptions"
        :recipe-folders-error="recipeFoldersError"
        :editing-recipe-id="editingRecipeId"
        :recipe-items="recipeItems"
        :is-default-create-recipe="isDefaultCreateRecipe"
        :format-recipe-summary="formatRecipeSummary"
        :shortcut-label-input-ref="shortcutLabelInputRef"
        @toggle-show-hidden="toggleShowHidden"
        @refresh-files-list="reloadFilesList"
        @toggle-folder-creator="toggleFolderCreator"
        @pick-upload-files="pickUploadFiles"
        @toggle-shortcut-editor="toggleShortcutEditor"
        @toggle-voice-mode="toggleVoiceMode"
        @toggle-terminal-size-editor="toggleTerminalSizeEditor"
        @update:terminal-size-draft-cols="terminalSizeDraftCols = $event"
        @update:terminal-size-draft-rows="terminalSizeDraftRows = $event"
        @apply-terminal-size="applyTerminalSize"
        @cancel-terminal-size-editor="cancelTerminalSizeEditor"
        @cancel-recipe-edit="cancelRecipeEdit"
        @add-new-recipe="addNewRecipe"
        @create-folder="createFolder"
        @update:folder-name="folderName = $event"
        @go-parent-dir="goParentDir"
        @open-file-entry="openFileEntry"
        @download-file-entry="downloadFileEntry"
        @update:shortcut-editor="shortcutEditor = $event"
        @add-shortcut-command="addShortcutCommand"
        @collapse-shortcut-editor="collapseShortcutEditor"
        @send-shortcut="sendShortcut"
        @update:recipe-editor="recipeEditor = $event"
        @submit-recipe-editor="submitRecipeEditor"
        @toggle-default-create-recipe="toggleDefaultCreateRecipe"
        @run-recipe="runRecipe"
        @edit-recipe="editRecipe"
        @remove-recipe="removeRecipe"
        @switch-right-tab="switchRightTab"
      />
    </div>

    <input ref="uploadFilesInputRef" class="hidden-input" type="file" multiple @change="onUploadFilesChange" />
    <textarea
      ref="voiceInputRef"
      v-model="voiceInputValue"
      class="voice-mode-input"
      data-testid="voice-mode-input"
      autocapitalize="off"
      autocomplete="off"
      autocorrect="off"
      spellcheck="false"
      @compositionstart="onVoiceCompositionStart"
      @compositionend="onVoiceCompositionEnd"
      @input="onVoiceInput"
    />
    <input v-model="command" data-testid="command-input" class="test-hook" />
    <input v-model="argsInput" data-testid="args-input" class="test-hook" />
    <input v-model="cwd" data-testid="cwd-input" class="test-hook" />
    <input v-model="envInput" data-testid="env-input" class="test-hook" />
    <select v-model="createNodeId" data-testid="node-select" class="test-hook">
      <option value="">Auto</option>
      <option v-for="node in terminalStore.nodes" :key="node.node_id" :value="node.node_id">
        {{ node.node_name || node.node_id }}
      </option>
    </select>
    <pre class="plain-output-probe" data-testid="plain-output">{{ plainOutputProbe }}</pre>
  </div>
</template>

<script setup>
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { Terminal } from 'xterm';
import { FitAddon } from '@xterm/addon-fit';
import 'xterm/css/xterm.css';
import { useWebCliTerminalStore } from '../stores/webcli-terminal.js';
import { useWebCliFilesStore } from '../stores/webcli-files.js';
import { useWebCliRecipesStore } from '../stores/webcli-recipes.js';
import { createTerminalProtocolRenderer } from '../composables/useTerminalProtocol.js';
import { useDesktopTerminalFileTabs } from '../composables/useDesktopTerminalFileTabs.js';
import FileEditorPanel from '../components/FileEditorPanel.vue';
import RightWorkspaceSidebar from '../components/RightWorkspaceSidebar.vue';
import TerminalSidebar from '../components/TerminalSidebar.vue';
import {
  isTerminalGeometryChanged,
  isTerminalViewportRenderable,
  measureStableTerminalGeometry,
  normalizeTerminalGeometry
} from './desktop-terminal-resize.js';
import { formatCommandLine, parseCommandLine } from '../utils/command-line.js';
import {
  BUILT_IN_SHORTCUT_ITEMS,
  COMBO_KEY_INTERVAL_MS,
  compareShortcutGroup,
  compressPath,
  CUSTOM_SHORTCUT_STORAGE_KEY,
  DEFAULT_CWD_PATH,
  DEFAULT_RECIPE_STORAGE_KEY,
  formatFileEntryTooltip,
  formatFileTabTooltip,
  formatInstanceSummary,
  formatInstanceTooltip,
  formatNodeOption,
  formatRecipeSummary,
  formatSize,
  normalizeShortcutGroup,
  parseJsonOrDefault,
  parseRecipeEnv,
  QUICK_COMMAND_INTERVAL_MS,
  toShortcutPayload,
  buildRecipeEditor as buildRecipeEditorState
} from '../utils/desktop-terminal.js';
import {
  DEFAULT_VOICE_COMMIT_DELAY_MS,
  VOICE_MODE_SHORTCUT_LABEL,
  isVoiceToggleShortcut
} from '../utils/voice-terminal.js';

const terminalStore = useWebCliTerminalStore();
const filesStore = useWebCliFilesStore();
const recipesStore = useWebCliRecipesStore();

const terminalRef = ref(null);
const uploadFilesInputRef = ref(null);

const command = ref('bash');
const argsInput = ref('["-i"]');
const envInput = ref('{}');
const cwd = ref('');
const createNodeId = ref('');
const cols = ref(120);
const rows = ref(34);

const activeCenterTab = ref('terminal');
const editingRecipeId = ref('');
const showRecipeEditor = ref(false);
const recipeEditor = ref(buildRecipeEditor());
const defaultCreateRecipeId = ref('');
const customShortcutItems = ref([]);
const showShortcutEditor = ref(false);
const showFolderCreator = ref(false);
const showTerminalSizeEditor = ref(false);
const terminalSizeDraftCols = ref('120');
const terminalSizeDraftRows = ref('34');
const folderName = ref('');
const recipeFolderOptions = ref([]);
const recipeFoldersLoading = ref(false);
const recipeFoldersError = ref('');
const shortcutEditor = ref({
  label: '',
  command: '',
  group: 'custom',
  pressEnter: true
});
const renamingInstanceId = ref('');
const renameInstanceValue = ref('');
const renameInstanceInputRef = ref(null);
const shortcutLabelInputRef = ref(null);
const plainTextContent = ref('');
const plainTextTitle = ref('纯文本');
const plainTextVisible = ref(false);
const plainOutputProbe = ref('');
const voiceInputRef = ref(null);
const voiceInputValue = ref('');
const voiceModeEnabled = ref(false);
const voiceModeShortcutLabel = VOICE_MODE_SHORTCUT_LABEL;
const voiceCompositionActive = ref(false);

let term = null;
let fitAddon = null;
let unsubscribe = null;
let renderer = null;
let suppressRemoteResize = false;
let lastMeasuredGeometry = null;
let terminalGeometryInitialized = false;
let terminalResizeObserver = null;
let pendingTerminalFitFrame = 0;
let terminalFitRequestId = 0;
let terminalInputElement = null;
let voiceCommitTimer = 0;

const recipeItems = computed(() => [...recipesStore.items].sort((a, b) => {
  const groupCompare = String(a.group || 'general').localeCompare(String(b.group || 'general'), 'zh-Hans-CN');
  if (groupCompare !== 0) {
    return groupCompare;
  }
  return String(a.name || a.command || '').localeCompare(String(b.name || b.command || ''), 'zh-Hans-CN');
}));

const currentTerminalName = computed(() => {
  const selected = terminalStore.selectedInstance;
  if (!selected) {
    return '未连接';
  }
  return formatInstanceDisplayName(selected);
});

function readTerminalPlainText() {
  if (!term?.buffer?.active) {
    return '';
  }
  const buffer = term.buffer.active;
  const length = Number(buffer.length) || 0;
  const lines = [];
  for (let index = 0; index < length; index += 1) {
    const line = buffer.getLine(index);
    if (!line || typeof line.translateToString !== 'function') {
      continue;
    }
    lines.push(line.translateToString(true));
  }
  return lines.join('\n');
}

const selectedNode = computed(() => {
  const targetId = String(createNodeId.value || '').trim();
  if (!targetId) {
    return null;
  }
  return terminalStore.nodes.find((item) => String(item?.node_id || '').trim() === targetId) || null;
});

const isSelectedNodeOnline = computed(() => selectedNode.value?.node_online !== false);

const visibleTerminalInstances = computed(() => {
  const targetId = String(createNodeId.value || '').trim();
  if (!targetId) {
    return terminalStore.instances;
  }
  return terminalStore.instances.filter((item) => String(item?.node_id || '').trim() === targetId);
});

const activeRightTab = computed(() => {
  const tab = String(terminalStore.uiSession.activeRightTab || 'files').trim();
  return ['files', 'shortcuts', 'recipes'].includes(tab) ? tab : 'files';
});
const rightTabTitle = computed(() => {
  if (activeRightTab.value === 'shortcuts') {
    return '快捷指令';
  }
  if (activeRightTab.value === 'recipes') {
    return '终端配方';
  }
  return '文件浏览器';
});
const rightTabIconClass = computed(() => {
  if (activeRightTab.value === 'shortcuts') {
    return 'fa-regular fa-keyboard';
  }
  if (activeRightTab.value === 'recipes') {
    return 'fa-regular fa-bookmark';
  }
  return 'fa-regular fa-folder-open';
});

const currentPathDisplay = computed(() => compressPath(filesStore.currentPath));

const defaultCreateRecipe = computed(() => {
  const targetId = String(defaultCreateRecipeId.value || '').trim();
  if (!targetId) {
    return null;
  }
  return recipesStore.items.find((item) => item.id === targetId) || null;
});

const createTerminalTitle = computed(() => {
  const recipe = defaultCreateRecipe.value;
  if (!recipe) {
    return '新建终端';
  }
  return `新建终端（默认配方：${recipe.name || recipe.command}）`;
});

const shortcutItems = computed(() => [...BUILT_IN_SHORTCUT_ITEMS, ...customShortcutItems.value]);

const shortcutGroups = computed(() => {
  const groups = new Map();
  for (const item of shortcutItems.value) {
    const group = normalizeShortcutGroup(item?.group);
    if (!groups.has(group)) {
      groups.set(group, []);
    }
    groups.get(group).push(item);
  }

  return Array.from(groups.entries())
    .sort((a, b) => compareShortcutGroup(a[0], b[0]))
    .map(([group, items]) => ({
      group,
      items
    }));
});

function getInstanceAlias(instanceId) {
  return terminalStore.getInstanceAlias(instanceId);
}

function buildRecipeEditor(seed = null) {
  return buildRecipeEditorState(seed, normalizeSelectableCwd, formatCommandLine);
}

function resolveHttpBase() {
  return String(import.meta.env.VITE_WEBPTY_BASE || '/web-pty').trim();
}

function buildApiPath(pathname) {
  return `${resolveHttpBase()}${pathname}`;
}

function buildNodeFileApiPath(nodeId, pathname, params = null) {
  const normalizedNodeId = String(nodeId || '').trim();
  const targetPath = normalizedNodeId
    ? `/api/nodes/${encodeURIComponent(normalizedNodeId)}/files${pathname}`
    : `/api/files${pathname}`;
  const url = new URL(buildApiPath(targetPath), typeof window !== 'undefined' ? window.location.origin : 'http://127.0.0.1');
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

function normalizeSelectableCwd(value) {
  const fallbackCwd = resolveDefaultSelectableCwd();
  const text = String(value || '').trim();
  if (!text) {
    return fallbackCwd;
  }
  return recipeFolderOptions.value.some((item) => item.path === text) ? text : fallbackCwd;
}

function resolveDefaultSelectableCwd() {
  return String(filesStore.basePath || filesStore.currentPath || DEFAULT_CWD_PATH).trim() || DEFAULT_CWD_PATH;
}

async function loadRecipeFolders(nodeId = createNodeId.value) {
  const targetNodeId = String(nodeId || '').trim();
  recipeFoldersLoading.value = true;
  recipeFoldersError.value = '';
  try {
    const response = await fetch(buildNodeFileApiPath(targetNodeId, '/list', {
      path: '',
      show_hidden: filesStore.showHidden ? '1' : '0'
    }));
    if (!response.ok) {
      throw new Error(await parseErrorMessage(response, `load recipe folders failed: ${response.status}`));
    }

    const payload = await response.json();
    const nodeBasePath = String(payload?.base || payload?.path || resolveDefaultSelectableCwd()).trim() || DEFAULT_CWD_PATH;
    const items = Array.isArray(payload?.items) ? payload.items : [];
    recipeFolderOptions.value = [
      {
        path: nodeBasePath,
        label: nodeBasePath.split('/').filter(Boolean).pop() || nodeBasePath
      },
      ...items
        .filter((item) => item?.kind === 'dir')
        .map((item) => ({
          path: String(item.path || '').trim(),
          label: String(item.name || item.path || '').trim() || String(item.path || '').trim()
        }))
        .filter((item) => item.path)
    ].filter((item, index, array) => array.findIndex((candidate) => candidate.path === item.path) === index);
    recipeEditor.value.cwd = normalizeSelectableCwd(recipeEditor.value.cwd);
  } catch (error) {
    recipeFoldersError.value = String(error?.message || error || 'load recipe folders failed');
    const fallbackCwd = resolveDefaultSelectableCwd();
    recipeFolderOptions.value = [{
      path: fallbackCwd,
      label: fallbackCwd.split('/').filter(Boolean).pop() || fallbackCwd
    }];
    recipeEditor.value.cwd = fallbackCwd;
  } finally {
    recipeFoldersLoading.value = false;
  }
}

function getActiveNodeId() {
  return String(createNodeId.value || terminalStore.selectedInstance?.node_id || terminalStore.getDefaultNodeId(createNodeId.value) || '').trim();
}

function hydrateCustomShortcuts() {
  try {
    const raw = localStorage.getItem(CUSTOM_SHORTCUT_STORAGE_KEY);
    if (!raw) {
      customShortcutItems.value = [];
      return;
    }
    const parsed = JSON.parse(raw);
    const items = Array.isArray(parsed?.items) ? parsed.items : [];
    customShortcutItems.value = items
      .map((item) => toShortcutPayload(item))
      .filter(Boolean);
  } catch {
    customShortcutItems.value = [];
  }
}

function persistCustomShortcuts() {
  const payload = customShortcutItems.value
    .map((item) => toShortcutPayload(item))
    .filter(Boolean);
  try {
    localStorage.setItem(CUSTOM_SHORTCUT_STORAGE_KEY, JSON.stringify({ items: payload }));
  } catch {
  }
}

function hydrateDefaultCreateRecipe() {
  try {
    const value = localStorage.getItem(DEFAULT_RECIPE_STORAGE_KEY);
    defaultCreateRecipeId.value = String(value || '').trim();
  } catch {
    defaultCreateRecipeId.value = '';
  }
}

function persistDefaultCreateRecipe() {
  const id = String(defaultCreateRecipeId.value || '').trim();
  try {
    if (!id) {
      localStorage.removeItem(DEFAULT_RECIPE_STORAGE_KEY);
      return;
    }
    localStorage.setItem(DEFAULT_RECIPE_STORAGE_KEY, id);
  } catch {
  }
}

function isDefaultCreateRecipe(recipeId) {
  return String(recipeId || '') === String(defaultCreateRecipeId.value || '');
}

function toggleDefaultCreateRecipe(item) {
  if (!item?.id) {
    return;
  }
  if (isDefaultCreateRecipe(item.id)) {
    defaultCreateRecipeId.value = '';
    persistDefaultCreateRecipe();
    terminalStore.setStatus('已取消 + 号默认配方');
    return;
  }

  defaultCreateRecipeId.value = String(item.id);
  persistDefaultCreateRecipe();
  terminalStore.setStatus(`已设置 + 号默认配方：${item.name || item.command}`);
}

function addShortcutCommand() {
  const label = String(shortcutEditor.value.label || '').trim();
  const commandText = String(shortcutEditor.value.command || '').trim();
  if (!label || !commandText) {
    terminalStore.setStatus('快捷指令名称和命令都不能为空');
    return;
  }

  const group = normalizeShortcutGroup(shortcutEditor.value.group);
  const pressEnter = shortcutEditor.value.pressEnter === true;
  const id = `custom-${Date.now()}-${Math.floor(Math.random() * 10000)}`;

  customShortcutItems.value.push({
    id,
    label,
    group,
    sequence: pressEnter ? [commandText, '\r'] : [commandText],
    intervalMs: QUICK_COMMAND_INTERVAL_MS
  });
  persistCustomShortcuts();

  shortcutEditor.value = {
    label: '',
    command: '',
    group,
    pressEnter
  };
  showShortcutEditor.value = false;
  terminalStore.setStatus(`已添加快捷指令：${label}`);
}

function collapseShortcutEditor() {
  showShortcutEditor.value = false;
}

function toggleShortcutEditor() {
  showShortcutEditor.value = !showShortcutEditor.value;
  if (showShortcutEditor.value) {
    nextTick(() => {
      shortcutLabelInputRef.value?.focus();
    });
  }
}

function focusTerminal() {
  if (voiceModeEnabled.value) {
    focusVoiceInput();
    return;
  }
  term?.focus();
  terminalInputElement?.focus?.({ preventScroll: true });
}

function focusVoiceInput() {
  voiceInputRef.value?.focus?.({ preventScroll: true });
}

function clearVoiceInputBuffer() {
  voiceInputValue.value = '';
  if (voiceInputRef.value && typeof voiceInputRef.value.value === 'string') {
    voiceInputRef.value.value = '';
  }
}

function cancelPendingVoiceCommit() {
  if (voiceCommitTimer) {
    clearTimeout(voiceCommitTimer);
    voiceCommitTimer = 0;
  }
}

function isVoiceToggleContextAvailable() {
  return activeCenterTab.value === 'terminal' && typeof document !== 'undefined';
}

function isEditableElement(target) {
  if (!(target instanceof HTMLElement)) {
    return false;
  }

  if (target.isContentEditable) {
    return true;
  }

  const tagName = String(target.tagName || '').toLowerCase();
  return tagName === 'input' || tagName === 'textarea' || tagName === 'select';
}

function isVoiceToggleBlockedContext() {
  if (!isVoiceToggleContextAvailable()) {
    return false;
  }

  const active = document.activeElement;
  if (!active) {
    return false;
  }

  if (active === voiceInputRef.value) {
    return false;
  }

  if (terminalRef.value?.contains?.(active) === true) {
    return false;
  }

  return isEditableElement(active);
}

function applyVoiceMode(next, statusMessage = '', options = {}) {
  const { preserveFocus = false, preserveBuffer = false } = options;
  voiceModeEnabled.value = next === true;
  voiceCompositionActive.value = false;
  cancelPendingVoiceCommit();
  if (!preserveBuffer) {
    clearVoiceInputBuffer();
  }
  nextTick(() => {
    if (preserveFocus) {
      return;
    }
    if (voiceModeEnabled.value) {
      focusVoiceInput();
    } else {
      focusTerminal();
    }
  });
  terminalStore.setStatus(statusMessage || (voiceModeEnabled.value ? '语音模式已开启' : '语音模式已关闭'));
}

function toggleVoiceMode() {
  applyVoiceMode(!voiceModeEnabled.value);
}

async function flushVoiceInput() {
  cancelPendingVoiceCommit();
  if (!voiceModeEnabled.value || voiceCompositionActive.value) {
    return;
  }

  const payload = String(voiceInputValue.value || '').trim();
  if (!payload) {
    clearVoiceInputBuffer();
    return;
  }

  if (!terminalStore.wsConnected || !terminalStore.selectedInstanceId) {
    terminalStore.setStatus('语音模式已激活，但当前没有已连接终端');
    return;
  }

  await terminalStore.sendInput(payload, { source: 'voice' });
  clearVoiceInputBuffer();
  nextTick(() => {
    if (voiceModeEnabled.value) {
      focusVoiceInput();
    }
  });
}

function appendVoiceInputText(text, options = {}) {
  const payload = String(text || '');
  if (!payload) {
    return;
  }
  voiceInputValue.value = `${String(voiceInputValue.value || '')}${payload}`;
  if (!options.deferCommit && !voiceCompositionActive.value) {
    scheduleVoiceCommit();
  }
}

function scheduleVoiceCommit() {
  cancelPendingVoiceCommit();
  voiceCommitTimer = setTimeout(() => {
    flushVoiceInput().catch((error) => {
      terminalStore.setStatus(String(error?.message || error || 'voice input failed'));
    });
  }, DEFAULT_VOICE_COMMIT_DELAY_MS);
}

function onVoiceCompositionStart() {
  voiceCompositionActive.value = true;
  cancelPendingVoiceCommit();
}

function onVoiceCompositionEnd() {
  voiceCompositionActive.value = false;
  scheduleVoiceCommit();
}

function onVoiceInput() {
  if (!voiceModeEnabled.value) {
    clearVoiceInputBuffer();
    return;
  }
  if (voiceCompositionActive.value) {
    return;
  }
  scheduleVoiceCommit();
}

function onGlobalTerminalKeyDown(event) {
  if (!isVoiceToggleContextAvailable()) {
    return;
  }
  if (!isVoiceToggleShortcut(event)) {
    return;
  }
  if (isVoiceToggleBlockedContext()) {
    return;
  }
  event.preventDefault();
  event.stopPropagation();
  applyVoiceMode(!voiceModeEnabled.value, voiceModeEnabled.value ? '语音模式已关闭' : '语音模式已开启');
}

function resolveTerminalInputElement() {
  terminalInputElement = terminalRef.value?.querySelector('.xterm-helper-textarea') || null;
  return terminalInputElement;
}

function bindTerminalInputHandlers() {
  const input = resolveTerminalInputElement();
  if (!input) {
    return;
  }

  input.setAttribute('aria-label', 'Terminal input');
  input.setAttribute('autocapitalize', 'off');
  input.setAttribute('autocomplete', 'off');
  input.setAttribute('autocorrect', 'off');
  input.setAttribute('spellcheck', 'false');
  input.addEventListener('paste', onTerminalPaste);
}

function unbindTerminalInputHandlers() {
  terminalInputElement?.removeEventListener('paste', onTerminalPaste);
  terminalInputElement = null;
}

function setTerminalDraftFromCurrent() {
  terminalSizeDraftCols.value = String(Math.max(1, Number(cols.value) || 120));
  terminalSizeDraftRows.value = String(Math.max(1, Number(rows.value) || 34));
}

function normalizeTerminalSizeInput(value, fallback) {
  const parsed = Math.floor(Number(value));
  if (!Number.isFinite(parsed) || parsed <= 0) {
    return Math.max(1, Math.floor(Number(fallback) || 1));
  }
  return parsed;
}

function updateStoredTerminalGeometry(nextCols, nextRows) {
  cols.value = Math.max(1, nextCols);
  rows.value = Math.max(1, nextRows);
  lastMeasuredGeometry = normalizeTerminalGeometry(cols.value, rows.value);
  terminalGeometryInitialized = true;
}

function applyLocalTerminalGeometry(nextCols, nextRows) {
  if (!term) {
    updateStoredTerminalGeometry(nextCols, nextRows);
    return;
  }

  suppressRemoteResize = true;
  try {
    if (typeof term.resize === 'function') {
      term.resize(nextCols, nextRows);
    }
  } finally {
    suppressRemoteResize = false;
  }
  updateStoredTerminalGeometry(nextCols, nextRows);
}

function toggleTerminalSizeEditor() {
  showTerminalSizeEditor.value = !showTerminalSizeEditor.value;
  if (showTerminalSizeEditor.value) {
    setTerminalDraftFromCurrent();
  }
}

function cancelTerminalSizeEditor() {
  showTerminalSizeEditor.value = false;
  setTerminalDraftFromCurrent();
}

async function applyTerminalSize() {
  const nextCols = normalizeTerminalSizeInput(terminalSizeDraftCols.value, cols.value || 120);
  const nextRows = normalizeTerminalSizeInput(terminalSizeDraftRows.value, rows.value || 34);

  terminalSizeDraftCols.value = String(nextCols);
  terminalSizeDraftRows.value = String(nextRows);
  applyLocalTerminalGeometry(nextCols, nextRows);
  showTerminalSizeEditor.value = false;

  if (terminalStore.wsConnected && terminalStore.selectedInstanceId) {
    try {
      terminalStore.sendResize(nextCols, nextRows);
      await terminalStore.resync();
      terminalStore.setStatus(`已更新伪终端尺寸：${nextCols} x ${nextRows}`);
    } catch (error) {
      terminalStore.setStatus(String(error?.message || error || 'resize failed'));
    }
  } else {
    terminalStore.setStatus(`已保存默认伪终端尺寸：${nextCols} x ${nextRows}`);
  }

  focusTerminal();
}

function getTerminalSelectionText() {
  return String(term?.getSelection?.() || '');
}

function hasTerminalSelection() {
  return getTerminalSelectionText().length > 0;
}

function isCopyKeyboardEvent(event) {
  if (!event) {
    return false;
  }
  const key = String(event.key || '').toLowerCase();
  return (event.ctrlKey || event.metaKey) && !event.altKey && key === 'c';
}

async function fitTerminal() {
  await nextTick();
  const measured = await measureStableTerminalGeometry({
    activeCenterTab: activeCenterTab.value,
    hostElement: terminalRef.value,
    fitAddon,
    term,
    isDocumentHidden: () => typeof document !== 'undefined' && document.hidden
  });

  if (measured?.cols > 0 && measured?.rows > 0) {
    updateStoredTerminalGeometry(measured.cols, measured.rows);
    return measured;
  }

  return null;
}

async function resolveCreateGeometry() {
  await primeCreateGeometryMeasurement();

  const measured = await measureStableTerminalGeometry({
    activeCenterTab: activeCenterTab.value,
    hostElement: terminalRef.value,
    fitAddon,
    term,
    isDocumentHidden: () => typeof document !== 'undefined' && document.hidden,
    attempts: 16,
    intervalMs: 50
  });
  if (measured?.cols > 0 && measured?.rows > 0) {
    try {
      fitAddon?.fit?.();
    } catch {
    }
    if (Number(term?.cols) !== measured.cols || Number(term?.rows) !== measured.rows) {
      applyLocalTerminalGeometry(measured.cols, measured.rows);
    } else {
      updateStoredTerminalGeometry(measured.cols, measured.rows);
    }
    return measured;
  }

  if (lastMeasuredGeometry?.cols > 0 && lastMeasuredGeometry?.rows > 0) {
    return {
      cols: lastMeasuredGeometry.cols,
      rows: lastMeasuredGeometry.rows
    };
  }

  return {
    cols: Math.max(1, Number(cols.value) || 1),
    rows: Math.max(1, Number(rows.value) || 1)
  };
}

function handleTerminalResize({ cols: c, rows: r }) {
  if (suppressRemoteResize || document.hidden || !isTerminalViewportRenderable(activeCenterTab.value, terminalRef.value)) {
    return;
  }

  updateStoredTerminalGeometry(c, r);
}

function syncVisibleTerminal(reason = 'visible') {
  if (activeCenterTab.value !== 'terminal' || !terminalStore.wsConnected || !terminalStore.selectedInstanceId) {
    return;
  }

  terminalStore.resync().catch(() => {
    terminalStore.setStatus(`screen sync failed: ${reason}`);
  });
}

function onDocumentVisibilityChange() {
  if (typeof document !== 'undefined' && document.hidden === false) {
    scheduleTerminalFit('visibility');
    syncVisibleTerminal('visibility');
  }
}

function switchCenterTab(tabId) {
  activeCenterTab.value = tabId;
  if (tabId === 'terminal') {
    nextTick(() => {
      scheduleTerminalFit('tab_switch');
      if (voiceModeEnabled.value) {
        focusVoiceInput();
      } else {
        focusTerminal();
      }
      syncVisibleTerminal('tab_switch');
    });
    return;
  }

  if (voiceModeEnabled.value) {
    applyVoiceMode(false);
  }
}

function switchRightTab(tabId) {
  const target = ['files', 'shortcuts', 'recipes'].includes(tabId) ? tabId : 'files';
  terminalStore.setUiSession({ activeRightTab: target });
}

const {
  fileTabs,
  activeFileTab,
  openFileEntry,
  closeFileTab,
  closeAllFileTabs,
  updateActiveFileContent,
  saveActiveFileTab,
  reloadFileTab,
  loadMoreFileTab,
  previewFileTabTail,
  loadFileTabFromStart,
  updateActiveImageZoom
} = useDesktopTerminalFileTabs({
  buildNodeFileApiPath,
  parseErrorMessage,
  filesStore,
  getActiveNodeId,
  switchCenterTab,
  activeCenterTab,
  setStatus: (message) => terminalStore.setStatus(message)
});

const showCloseAllFilesEntry = computed(() => fileTabs.value.length > 1);
const closeAllFilesTitle = computed(() => `关闭全部文件标签（当前 ${fileTabs.value.length} 个）`);

async function onTargetNodeChange(nodeId) {
  createNodeId.value = String(nodeId || '').trim();
  try {
    await loadRecipeFolders(createNodeId.value);
    await syncNodeTerminalSelection();
  } catch (error) {
    terminalStore.setStatus(String(error?.message || error || 'switch node failed'));
  }
}

function formatInstanceDisplayName(instance) {
  const instanceAlias = terminalStore.getInstanceAlias(instance?.id);
  if (instanceAlias) {
    return instanceAlias;
  }
  return formatInstanceSummary(instance);
}

function setRenameInstanceInputRef(element) {
  renameInstanceInputRef.value = element;
}

function beginRenameInstance(instance) {
  renamingInstanceId.value = String(instance?.id || '').trim();
  renameInstanceValue.value = terminalStore.getInstanceAlias(instance?.id) || '';
  nextTick(() => {
    renameInstanceInputRef.value?.focus?.();
    renameInstanceInputRef.value?.select?.();
  });
}

function cancelRenameInstance() {
  renamingInstanceId.value = '';
  renameInstanceValue.value = '';
  renameInstanceInputRef.value = null;
}

function saveRenameInstance(instanceId) {
  const id = String(instanceId || '').trim();
  if (!id || renamingInstanceId.value !== id) {
    return;
  }
  terminalStore.setInstanceAlias(id, renameInstanceValue.value);
  const label = terminalStore.getInstanceAlias(id);
  terminalStore.setStatus(label ? `已更新会话名：${label}` : `已清除会话名：${id}`);
  cancelRenameInstance();
}

async function loadFilesForSelected() {
  const nodeId = getActiveNodeId();
  const path = terminalStore.selectedInstance?.cwd || filesStore.currentPath || filesStore.basePath || '';
  await filesStore.loadList(path, nodeId);
}

async function loadNodeFilesRoot(nodeId = getActiveNodeId()) {
  await filesStore.loadList('', nodeId);
}

async function connect(instanceId) {
  const id = String(instanceId || '').trim();
  if (!id) {
    return;
  }
  try {
    const measured = await syncTerminalFit('connect_prepare', { syncRemote: false });
    const nextCols = Math.max(1, Number(measured?.cols) || Number(cols.value) || 120);
    const nextRows = Math.max(1, Number(measured?.rows) || Number(rows.value) || 34);
    if (nextCols <= 0 || nextRows <= 0) {
      terminalStore.setStatus('Terminal viewport is not ready');
      return;
    }

    await terminalStore.connect(id);
    applyLocalTerminalGeometry(nextCols, nextRows);
    if (activeCenterTab.value === 'terminal') {
      focusTerminal();
    }
    loadFilesForSelected().catch(() => {});
  } catch (error) {
    terminalStore.setStatus(String(error?.message || error || 'connect failed'));
  }
}

async function refreshTerminals() {
  try {
    await Promise.all([
      terminalStore.fetchNodes(),
      terminalStore.fetchInstances()
    ]);

    createNodeId.value = String(
      terminalStore.resolvePreferredNodeId(createNodeId.value || terminalStore.selectedInstance?.node_id || '')
    );
    await loadRecipeFolders(createNodeId.value);

    if (terminalStore.instances.length === 0) {
      terminalStore.disconnect();
      terminalStore.selectedInstanceId = '';
      await loadNodeFilesRoot(getActiveNodeId());
      return;
    }

    await syncNodeTerminalSelection();
  } catch (error) {
    terminalStore.setStatus(String(error?.message || error || 'refresh failed'));
  }
}

async function syncNodeTerminalSelection() {
  const targetId = String(createNodeId.value || '').trim();
  const visibleItems = targetId
    ? terminalStore.instances.filter((item) => String(item?.node_id || '').trim() === targetId)
    : terminalStore.instances;

  if (visibleItems.length === 0) {
    terminalStore.disconnect();
    terminalStore.selectedInstanceId = '';
    term?.reset?.();
    await loadNodeFilesRoot(targetId);
    terminalStore.setStatus(targetId ? '当前节点暂无终端会话' : '暂无终端会话');
    return;
  }

  const selectedVisible = visibleItems.some((item) => item.id === terminalStore.selectedInstanceId);
  const nextId = selectedVisible ? terminalStore.selectedInstanceId : visibleItems[0].id;
  await connect(nextId);
}

async function createInstance() {
  try {
    if (selectedNode.value?.node_online === false) {
      terminalStore.setStatus(`节点 ${selectedNode.value.node_name || selectedNode.value.node_id} 当前离线，无法新建终端`);
      return;
    }

    const geometry = await resolveCreateGeometry();
    const selectedRecipe = defaultCreateRecipe.value;
    const parsedArgs = selectedRecipe
      ? (Array.isArray(selectedRecipe.args) ? selectedRecipe.args.map((x) => String(x)) : [])
      : (() => {
          const parsed = parseJsonOrDefault(argsInput.value, ['-i']);
          return Array.isArray(parsed) ? parsed.map((x) => String(x)) : ['-i'];
        })();
    const parsedEnv = selectedRecipe
      ? (selectedRecipe.env && typeof selectedRecipe.env === 'object' && !Array.isArray(selectedRecipe.env) ? selectedRecipe.env : {})
      : (() => {
          const parsed = parseJsonOrDefault(envInput.value, {});
          return parsed && typeof parsed === 'object' && !Array.isArray(parsed) ? parsed : {};
        })();
    const commandText = selectedRecipe
      ? (String(selectedRecipe.command || '').trim() || 'bash')
      : (String(command.value || 'bash').trim() || 'bash');
    const fallbackCwd = terminalStore.selectedInstance?.cwd || filesStore.currentPath || filesStore.basePath || resolveDefaultSelectableCwd();
    const resolvedCwd = selectedRecipe
      ? normalizeSelectableCwd(selectedRecipe.cwd || fallbackCwd)
      : normalizeSelectableCwd(cwd.value || fallbackCwd);
    const nodeId = getActiveNodeId();

    const created = await terminalStore.createInstance({
      command: commandText,
      args: parsedArgs,
      env: parsedEnv,
      cols: geometry.cols,
      rows: geometry.rows,
      cwd: resolvedCwd || undefined
    }, nodeId);

    if (created?.instance_id) {
      await terminalStore.fetchInstances();
      await connect(created.instance_id);
      if (selectedRecipe) {
        terminalStore.setStatus(`Connected (+默认配方: ${selectedRecipe.name || selectedRecipe.command})`);
      } else {
        terminalStore.setStatus('Connected (default recipe)');
      }
    }
  } catch (error) {
    terminalStore.setStatus(String(error?.message || error || 'create failed'));
  }
}

async function closeTerminal(instanceId) {
  const id = String(instanceId || '').trim();
  if (!id) {
    return;
  }

  try {
    const wasSelected = terminalStore.selectedInstanceId === id;
    await terminalStore.terminateInstance(id);
    terminalStore.clearInstanceAlias(id);
    if (wasSelected) {
      terminalStore.disconnect();
    }

    await terminalStore.fetchInstances();
    if (terminalStore.instances.length > 0) {
      await syncNodeTerminalSelection();
    } else {
      terminalStore.selectedInstanceId = '';
      await loadNodeFilesRoot(getActiveNodeId());
      terminalStore.setStatus(`Terminated ${id}`);
    }
  } catch (error) {
    terminalStore.setStatus(String(error?.message || error || 'terminate failed'));
  }
}

async function syncTerminalItem(instanceId) {
  const id = String(instanceId || '').trim();
  if (!id) {
    return;
  }
  try {
    if (terminalStore.selectedInstanceId !== id) {
      await connect(id);
    }
    await terminalStore.resync();
    terminalStore.setStatus(`已同步终端：${formatInstanceDisplayName(terminalStore.selectedInstance || { id })}`);
  } catch (error) {
    terminalStore.setStatus(String(error?.message || error || 'sync failed'));
  }
}

function closePlainTextView() {
  plainTextVisible.value = false;
  plainTextTitle.value = '纯文本';
  plainTextContent.value = '';
  if (activeCenterTab.value === 'plain-text') {
    activeCenterTab.value = 'terminal';
  }
}

async function copyPlainText() {
  if (!plainTextContent.value) {
    return;
  }
  try {
    await writeClipboardText(plainTextContent.value);
    terminalStore.setStatus('已复制纯文本');
  } catch (error) {
    terminalStore.setStatus(String(error?.message || error || 'copy plain text failed'));
  }
}

async function viewPlainTextItem(instanceId) {
  const id = String(instanceId || '').trim();
  if (!id) {
    return;
  }
  try {
    if (terminalStore.selectedInstanceId !== id) {
      await connect(id);
    } else {
      await terminalStore.resync();
    }

    const historyLines = await terminalStore.getPlainTextHistory(id);
    const visibleText = readTerminalPlainText();
    const combined = [...historyLines];
    if (visibleText) {
      combined.push(visibleText);
    }
    plainTextContent.value = combined.join('\n').replace(/\n{3,}/g, '\n\n');
    plainOutputProbe.value = plainTextContent.value;
    plainTextTitle.value = `${formatInstanceDisplayName(terminalStore.selectedInstance || { id })} 纯文本`;
    plainTextVisible.value = true;
    activeCenterTab.value = 'plain-text';
    terminalStore.setStatus(`已加载纯文本：${formatInstanceDisplayName(terminalStore.selectedInstance || { id })}`);
  } catch (error) {
    terminalStore.setStatus(String(error?.message || error || 'view plain text failed'));
  }
}

async function goParentDir() {
  if (!filesStore.parentPath) {
    return;
  }
  await filesStore.loadList(filesStore.parentPath, getActiveNodeId());
}

async function reloadFilesList() {
  await filesStore.loadList(filesStore.currentPath, getActiveNodeId());
}

function toggleFolderCreator() {
  showFolderCreator.value = !showFolderCreator.value;
  if (!showFolderCreator.value) {
    folderName.value = '';
  }
}

async function createFolder() {
  const name = String(folderName.value || '').trim();
  if (!name) {
    terminalStore.setStatus('文件夹名称不能为空');
    return;
  }

  try {
    await filesStore.createDirectory(name, filesStore.currentPath, getActiveNodeId());
    folderName.value = '';
    showFolderCreator.value = false;
    terminalStore.setStatus(`已创建文件夹：${name}`);
  } catch (error) {
    terminalStore.setStatus(String(error?.message || error || 'mkdir failed'));
  }
}

async function toggleShowHidden() {
  filesStore.showHidden = !filesStore.showHidden;
  await reloadFilesList();
}

async function downloadFileEntry(item) {
  if (!item?.path) {
    return;
  }
  try {
    await filesStore.downloadEntry(item.path, getActiveNodeId());
  } catch {
  }
}

function pickUploadFiles() {
  uploadFilesInputRef.value?.click();
}

async function onUploadFilesChange(event) {
  try {
    await filesStore.uploadFiles(event?.target?.files, filesStore.currentPath, getActiveNodeId());
  } catch {
  } finally {
    if (event?.target) {
      event.target.value = '';
    }
  }
}

async function runRecipe(item) {
  try {
    const geometry = await resolveCreateGeometry();
    const args = Array.isArray(item?.args) ? item.args.map((x) => String(x)) : [];
    const env = item?.env && typeof item.env === 'object' && !Array.isArray(item.env) ? item.env : {};
    const recipeCommand = String(item?.command || '').trim() || 'bash';
    const fallbackCwd = terminalStore.selectedInstance?.cwd || filesStore.currentPath || filesStore.basePath || resolveDefaultSelectableCwd();
    const recipeCwd = normalizeSelectableCwd(item?.cwd || fallbackCwd);
    const nodeId = getActiveNodeId();

    const created = await terminalStore.createInstance({
      command: recipeCommand,
      args,
      env,
      cols: geometry.cols,
      rows: geometry.rows,
      cwd: recipeCwd || undefined
    }, nodeId);

    if (created?.instance_id) {
      await terminalStore.fetchInstances();
      await connect(created.instance_id);
      terminalStore.setStatus(`Recipe started: ${item.name || recipeCommand}`);
      switchCenterTab('terminal');
    }
  } catch (error) {
    terminalStore.setStatus(String(error?.message || error || 'run recipe failed'));
  }
}

function addNewRecipe() {
  showRecipeEditor.value = true;
  editingRecipeId.value = '';
  const parsedArgs = parseJsonOrDefault(argsInput.value, ['-i']);
  recipeEditor.value = buildRecipeEditor({
    cwd: normalizeSelectableCwd(cwd.value || resolveDefaultSelectableCwd()),
    command: command.value,
    args: Array.isArray(parsedArgs) ? parsedArgs.map((item) => String(item)) : ['-i'],
    env: parseJsonOrDefault(envInput.value, {}),
    group: 'custom'
  });
}

function editRecipe(item) {
  showRecipeEditor.value = true;
  editingRecipeId.value = item.id;
  recipeEditor.value = buildRecipeEditor(item);
}

function cancelRecipeEdit() {
  showRecipeEditor.value = false;
  editingRecipeId.value = '';
  recipeEditor.value = buildRecipeEditor();
}

function submitRecipeEditor() {
  try {
    const parsedCommand = parseCommandLine(recipeEditor.value.commandLine);
    const env = parseRecipeEnv(recipeEditor.value.envInput);
    const selectedCwd = normalizeSelectableCwd(recipeEditor.value.cwd);
    if (!selectedCwd) {
      throw new Error('请选择文件夹');
    }

    const payload = {
      name: String(recipeEditor.value.name || '').trim() || parsedCommand.command,
      group: String(recipeEditor.value.group || 'general').trim() || 'general',
      cwd: selectedCwd,
      command: parsedCommand.command,
      args: parsedCommand.args,
      env
    };

    if (editingRecipeId.value) {
      recipesStore.updateRecipe(editingRecipeId.value, payload);
      terminalStore.setStatus('配方已更新');
    } else {
      recipesStore.addRecipe(payload);
      terminalStore.setStatus('配方已保存');
    }

    cancelRecipeEdit();
  } catch (error) {
    terminalStore.setStatus(String(error?.message || error || 'save recipe failed'));
  }
}

function removeRecipe(id) {
  if (!window.confirm('确认删除该配方？')) {
    return;
  }
  try {
    const removedDefault = isDefaultCreateRecipe(id);
    recipesStore.removeRecipe(id);
    if (removedDefault) {
      defaultCreateRecipeId.value = '';
      persistDefaultCreateRecipe();
    }
    if (editingRecipeId.value === id) {
      cancelRecipeEdit();
    }
  } catch (error) {
    terminalStore.setStatus(String(error?.message || error || 'delete recipe failed'));
  }
}

function saveCurrentAsRecipe() {
  try {
    const args = parseJsonOrDefault(argsInput.value, ['-i']);
    const env = parseJsonOrDefault(envInput.value, {});
    showRecipeEditor.value = true;
    recipeEditor.value = buildRecipeEditor({
      name: '',
      cwd: normalizeSelectableCwd(cwd.value || terminalStore.selectedInstance?.cwd || filesStore.currentPath || filesStore.basePath || resolveDefaultSelectableCwd()),
      command: String(command.value || 'bash').trim() || 'bash',
      args: Array.isArray(args) ? args.map((item) => String(item)) : ['-i'],
      env,
      group: 'quick'
    });
    editingRecipeId.value = '';
    terminalStore.setStatus('已填充当前配置，请点击“保存配方”确认');
  } catch (error) {
    terminalStore.setStatus(String(error?.message || error || 'prepare recipe failed'));
  }
}

function onTerminalPaste(event) {
  event.preventDefault();
  event.stopPropagation();

  const text = event?.clipboardData?.getData('text');
  if (text) {
    terminalStore.sendBracketedPaste(text).finally(() => {
      focusTerminal();
    });
    return;
  }

  readClipboardText()
    .then((value) => {
      if (!value) {
        return;
      }
      return terminalStore.sendBracketedPaste(value);
    })
    .catch(() => {})
    .finally(() => {
      focusTerminal();
    });
}

async function writeClipboardText(text) {
  if (navigator.clipboard?.writeText) {
    await navigator.clipboard.writeText(text);
    return;
  }
  const helper = document.createElement('textarea');
  helper.value = text;
  helper.setAttribute('readonly', 'readonly');
  helper.style.position = 'fixed';
  helper.style.left = '-9999px';
  document.body.appendChild(helper);
  helper.select();
  document.execCommand('copy');
  document.body.removeChild(helper);
}

async function readClipboardText() {
  if (navigator.clipboard?.readText) {
    return navigator.clipboard.readText();
  }
  return '';
}

function onTerminalCopy(event) {
  const selected = getTerminalSelectionText();
  if (!selected) {
    return;
  }

  event.preventDefault();
  if (event?.clipboardData?.setData) {
    event.clipboardData.setData('text/plain', selected);
    return;
  }

  writeClipboardText(selected).catch(() => {});
}

function onTerminalContextMenu(event) {
  event.preventDefault();

  const selected = getTerminalSelectionText();
  if (selected) {
    writeClipboardText(selected).catch(() => {}).finally(() => {
      term?.clearSelection?.();
      focusTerminal();
    });
    return;
  }

  readClipboardText()
    .then((text) => {
      if (!text) {
        return;
      }
      return terminalStore.sendBracketedPaste(text);
    })
    .catch(() => {})
    .finally(() => {
      focusTerminal();
  });
}

function onTerminalKeyEvent(event) {
  if (voiceModeEnabled.value && !isCopyKeyboardEvent(event)) {
    event.preventDefault();
    return false;
  }

  if (!isCopyKeyboardEvent(event) || !hasTerminalSelection()) {
    return true;
  }

  if (event.type === 'keydown') {
    writeClipboardText(getTerminalSelectionText()).catch(() => {});
  }

  event.preventDefault();
  return false;
}

function wait(ms) {
  if (ms <= 0) {
    return Promise.resolve();
  }
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function waitForAnimationFrame() {
  if (typeof window !== 'undefined' && typeof window.requestAnimationFrame === 'function') {
    return new Promise((resolve) => {
      window.requestAnimationFrame(() => resolve());
    });
  }
  return wait(16);
}

async function primeCreateGeometryMeasurement() {
  await nextTick();
  if (!isTerminalViewportRenderable(activeCenterTab.value, terminalRef.value)) {
    return;
  }

  try {
    fitAddon?.fit?.();
  } catch {
  }
  await waitForAnimationFrame();

  try {
    fitAddon?.fit?.();
  } catch {
  }
  await waitForAnimationFrame();
}

function cancelPendingTerminalFit() {
  if (typeof window !== 'undefined' && pendingTerminalFitFrame) {
    window.cancelAnimationFrame(pendingTerminalFitFrame);
  }
  pendingTerminalFitFrame = 0;
}

async function syncTerminalFit(reason = 'layout', options = {}) {
  const { syncRemote = true, requestId = ++terminalFitRequestId } = options;
  const measured = await fitTerminal();
  if (!measured || requestId !== terminalFitRequestId) {
    return null;
  }

  if (!isTerminalGeometryChanged(lastMeasuredGeometry, measured)) {
    return measured;
  }

  applyLocalTerminalGeometry(measured.cols, measured.rows);
  if (syncRemote && terminalStore.wsConnected && terminalStore.selectedInstanceId) {
    terminalStore.sendResize(measured.cols, measured.rows);
  }
  return measured;
}

function scheduleTerminalFit(reason = 'layout', options = {}) {
  const requestId = ++terminalFitRequestId;
  const run = () => {
    pendingTerminalFitFrame = 0;
    syncTerminalFit(reason, { ...options, requestId }).catch(() => {});
  };

  cancelPendingTerminalFit();
  if (typeof window !== 'undefined' && typeof window.requestAnimationFrame === 'function') {
    pendingTerminalFitFrame = window.requestAnimationFrame(run);
    return;
  }

  run();
}

async function sendShortcut(item) {
  const sequence = Array.isArray(item?.sequence) ? item.sequence : [item?.value];
  const payloads = sequence.map((value) => String(value ?? '')).filter((value) => value.length > 0);
  if (payloads.length === 0) {
    focusTerminal();
    return;
  }

  const configuredInterval = Number(item?.intervalMs);
  const intervalMs = Number.isFinite(configuredInterval)
    ? Math.max(0, Math.floor(configuredInterval))
    : (payloads.length > 1 ? COMBO_KEY_INTERVAL_MS : 0);

  try {
    for (let index = 0; index < payloads.length; index += 1) {
      await terminalStore.sendInput(payloads[index]);
      if (index < payloads.length - 1 && intervalMs > 0) {
        await wait(intervalMs);
      }
    }
  } finally {
    focusTerminal();
  }
}

onMounted(async () => {
  recipesStore.hydrate();
  hydrateDefaultCreateRecipe();
  hydrateCustomShortcuts();
  setTerminalDraftFromCurrent();
  if (defaultCreateRecipeId.value && !defaultCreateRecipe.value) {
    defaultCreateRecipeId.value = '';
    persistDefaultCreateRecipe();
  }

  term = new Terminal({
    convertEol: true,
    cursorBlink: true,
    fontFamily: 'JetBrains Mono, monospace',
    fontSize: 14,
    scrollback: 5000,
    theme: {
      background: '#0d0d0d',
      foreground: '#d4d4d4'
    }
  });

  fitAddon = new FitAddon();
  term.loadAddon(fitAddon);
  term.open(terminalRef.value);
  bindTerminalInputHandlers();
  term.attachCustomKeyEventHandler(onTerminalKeyEvent);
  if (typeof window !== 'undefined' && typeof window.__WEBCLI_DESKTOP_TERM_HOOK__ === 'function') {
    window.__WEBCLI_DESKTOP_TERM_HOOK__(term);
  }

  renderer = createTerminalProtocolRenderer(term);
  unsubscribe = terminalStore.subscribe((message) => {
    renderer.onMessage(message);
  });

  term.onData((data) => terminalStore.sendInput(data, { source: 'terminal' }));
  term.onResize(handleTerminalResize);
  terminalRef.value?.addEventListener('copy', onTerminalCopy);
  terminalRef.value?.addEventListener('paste', onTerminalPaste);
  terminalRef.value?.addEventListener('contextmenu', onTerminalContextMenu);
  terminalRef.value?.addEventListener('mousedown', focusTerminal);
  document.addEventListener('visibilitychange', onDocumentVisibilityChange);
  document.addEventListener('keydown', onGlobalTerminalKeyDown, true);
  if (typeof window !== 'undefined') {
    window.addEventListener('resize', scheduleTerminalFit);
  }
  if (typeof ResizeObserver !== 'undefined' && terminalRef.value) {
    terminalResizeObserver = new ResizeObserver(() => {
      scheduleTerminalFit('observer');
    });
    terminalResizeObserver.observe(terminalRef.value);
  }

  await syncTerminalFit('mount', { syncRemote: false });
  await Promise.all([
    refreshTerminals()
  ]);
  if (!createNodeId.value) {
    createNodeId.value = String(terminalStore.resolvePreferredNodeId(terminalStore.selectedInstance?.node_id || ''));
  }
  await loadRecipeFolders(createNodeId.value);
  if (voiceModeEnabled.value) {
    focusVoiceInput();
  } else {
    focusTerminal();
  }
});

onBeforeUnmount(() => {
  cancelPendingTerminalFit();
  cancelPendingVoiceCommit();
  unsubscribe?.();
  terminalStore.disconnect();
  document.removeEventListener('visibilitychange', onDocumentVisibilityChange);
  document.removeEventListener('keydown', onGlobalTerminalKeyDown, true);
  if (typeof window !== 'undefined') {
    window.removeEventListener('resize', scheduleTerminalFit);
  }
  unbindTerminalInputHandlers();
  terminalResizeObserver?.disconnect?.();
  terminalResizeObserver = null;
  terminalRef.value?.removeEventListener('copy', onTerminalCopy);
  terminalRef.value?.removeEventListener('paste', onTerminalPaste);
  terminalRef.value?.removeEventListener('contextmenu', onTerminalContextMenu);
  terminalRef.value?.removeEventListener('mousedown', focusTerminal);
  term?.dispose();
});

watch(activeCenterTab, (tabId) => {
  if (tabId === 'terminal') {
    scheduleTerminalFit('watch_tab');
  }
});
</script>

<style scoped>
* {
  margin: 0;
  padding: 0;
  box-sizing: border-box;
}

.app {
  --terminal-display-width: 1450px;
  --terminal-padding: 8px;
  --terminal-scrollbar-width: 55px;
  --terminal-shell-width: calc(var(--terminal-display-width) + (var(--terminal-padding) * 2) + var(--terminal-scrollbar-width));
  font-family: 'Inter', sans-serif;
  background-color: #1e1e1e;
  min-height: 100vh;
  min-height: 100dvh;
  height: 100vh;
  height: 100dvh;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  color: #e0e0e0;
}

.toolbar {
  background-color: #2d2d2d;
  padding: 8px 16px;
  display: flex;
  align-items: center;
  gap: 16px;
  border-bottom: 1px solid #3c3c3c;
  flex-shrink: 0;
}

.toolbar .logo {
  font-weight: 600;
  font-size: 1.1rem;
  color: #ccc;
  display: flex;
  align-items: center;
  gap: 6px;
}

.toolbar .logo i {
  color: #007acc;
}

.plain-text-panel {
  display: flex;
  flex: 1;
  min-height: 0;
  flex-direction: column;
  background: #111;
}

.plain-text-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 10px 14px;
  border-bottom: 1px solid #2f2f2f;
}

.plain-text-toolbar-title {
  font-size: 0.9rem;
  color: #d7d7d7;
}

.plain-text-content {
  flex: 1;
  min-height: 0;
  margin: 0;
  padding: 16px;
  overflow: auto;
  font-family: 'JetBrains Mono', monospace;
  font-size: 13px;
  line-height: 1.55;
  color: #d8d8d8;
  white-space: pre-wrap;
  word-break: break-word;
}

.plain-output-probe {
  display: none;
}

button,
input,
textarea,
select {
  font-family: 'Inter', sans-serif;
}

button {
  width: auto;
  background-color: #3c3c3c;
  border: 1px solid transparent;
  color: #e0e0e0;
  font-size: 0.86rem;
  padding: 6px 12px;
  border-radius: 4px;
  display: inline-flex;
  align-items: center;
  gap: 6px;
  cursor: pointer;
  transition: background 0.15s;
}

button:hover {
  background-color: #4a4a4a;
}

button.primary {
  background-color: #0e639c;
}

button.primary:hover {
  background-color: #1177bb;
}

.main {
  display: grid;
  grid-template-columns: minmax(230px, 320px) minmax(560px, 1fr) minmax(260px, 320px);
  flex: 1;
  overflow: hidden;
  min-height: 0;
  min-width: 0;
}

.sidebar-column {
  background-color: #252526;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  min-width: 0;
}

.left-sidebar {
  border-right: 1px solid #3c3c3c;
}

.right-sidebar {
  border-left: 1px solid #3c3c3c;
}

.terminal-panel {
  background-color: #1e1e1e;
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: 12px 14px;
  overflow: hidden;
  min-height: 0;
  min-width: 0;
}

.terminal-panel-content {
  display: flex;
  flex: 1 1 auto;
  flex-direction: column;
  min-height: 0;
  min-width: 0;
  width: min(100%, var(--terminal-shell-width));
  max-width: var(--terminal-shell-width);
  margin: 0 auto;
  overflow: hidden;
}

.terminal-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 10px;
  color: #bbb;
  font-size: 0.9rem;
  border-bottom: 1px solid #3c3c3c;
  padding-bottom: 8px;
  gap: 8px;
}

.terminal-tabs {
  display: flex;
  gap: 6px;
  align-items: center;
  min-width: 0;
  overflow-x: auto;
}

.tab-btn {
  background-color: #2d2d2d;
  color: #d7d7d7;
  border: 1px solid #414141;
  border-radius: 16px;
  padding: 4px 10px;
  display: inline-flex;
  align-items: center;
  gap: 6px;
  min-width: 0;
  max-width: 420px;
}

.tab-btn.active {
  background-color: #134a74;
  border-color: #0e639c;
  color: #fff;
}

.tabs-action-btn {
  flex: 0 0 auto;
  background-color: #3a3322;
  border-color: #6f5a2a;
  color: #f0d89a;
}

.tabs-action-btn:hover {
  background-color: #4a4029;
  color: #fff1c9;
}

.file-tab .tab-text {
  flex: 1 1 auto;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.file-tab .close-icon {
  font-size: 0.7rem;
  opacity: 0.7;
  flex: 0 0 auto;
}

.file-tab .close-icon:hover {
  opacity: 1;
}

.status-text {
  color: #6a9955;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.status-text i {
  margin-right: 4px;
}

.terminal-viewport {
  background-color: #0d0d0d;
  border-radius: 6px;
  width: 100%;
  max-width: 100%;
  flex: 1 1 auto;
  overflow: hidden;
  box-shadow: inset 0 0 8px rgba(0, 0, 0, 0.6);
  min-height: 0;
  height: 100%;
  max-height: none;
  padding: var(--terminal-padding);
}

.terminal-host {
  position: relative;
  height: 100%;
  min-height: 0;
  max-width: 100%;
  width: calc(100% - var(--terminal-scrollbar-width));
}


.hidden-input,
.test-hook {
  display: none;
}

.voice-mode-input {
  position: fixed;
  left: 12px;
  bottom: 12px;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: 0;
  border: 0;
  opacity: 0.01;
  resize: none;
  pointer-events: none;
  background: transparent;
  color: transparent;
}

.terminal-viewport :deep(.xterm) {
  position: relative;
  height: 100%;
  width: 100%;
  max-width: 100%;
  overflow: visible;
}

.terminal-viewport :deep(.xterm .xterm-helper-textarea) {
  left: 0;
  top: 0;
  width: 1px;
  height: 1px;
  opacity: 0.01;
  z-index: 1;
}

.terminal-viewport :deep(.xterm-screen) {
  width: 100%;
}

.terminal-viewport :deep(.xterm-viewport) {
  scrollbar-width: auto;
  position: absolute;
  top: 0;
  right: calc(-1 * var(--terminal-scrollbar-width));
  bottom: 0;
  left: 0;
  width: auto;
  max-width: none;
}

.terminal-viewport :deep(.xterm-viewport::-webkit-scrollbar) {
  width: var(--terminal-scrollbar-width);
  height: var(--terminal-scrollbar-width);
}

.terminal-viewport :deep(.xterm-viewport::-webkit-scrollbar-track) {
  background: #2d2d2d;
}

.terminal-viewport :deep(.xterm-viewport::-webkit-scrollbar-thumb) {
  background: #5a5a5a;
  border-radius: 10px;
  border: 12px solid #2d2d2d;
}

.terminal-viewport :deep(.xterm-viewport::-webkit-scrollbar-thumb:hover) {
  background: #7a7a7a;
}

@media (max-width: 1380px) {
  .main {
    grid-template-columns: minmax(210px, 280px) minmax(500px, 1fr) minmax(240px, 300px);
  }
}

@media (max-width: 980px) {
  .toolbar {
    flex-wrap: wrap;
  }

  .main {
    grid-template-columns: minmax(220px, 300px) minmax(0, 1fr);
  }

  .right-sidebar {
    grid-column: 1 / -1;
    border-left: none;
    border-top: 1px solid #3c3c3c;
    max-height: 38vh;
  }

  .terminal-panel {
    padding: 10px;
  }

  .terminal-panel-content,
  .terminal-viewport,
  .terminal-host,
  .terminal-viewport :deep(.xterm),
  .terminal-viewport :deep(.xterm-viewport) {
    width: 100%;
    max-width: 100%;
  }
}

@media (max-width: 820px) {
  .app {
    min-height: 100vh;
    min-height: 100dvh;
    height: auto;
    overflow-y: auto;
  }

  .toolbar {
    padding: 10px 12px;
    gap: 10px;
  }

  .toolbar .logo {
    font-size: 0.98rem;
  }

  .main {
    display: flex;
    flex-direction: column;
    min-height: 0;
    overflow: visible;
  }

  .left-sidebar,
  .right-sidebar {
    width: 100%;
    border-right: none;
    border-left: none;
    border-top: none;
    border-bottom: 1px solid #3c3c3c;
    max-height: none;
    flex: 0 0 auto;
  }

  .left-sidebar {
    min-height: 34dvh;
  }

  .terminal-panel {
    flex: 0 0 auto;
    min-height: 0;
    min-height: 42dvh;
    padding: 8px;
  }

  .right-sidebar {
    min-height: 38dvh;
    border-bottom: none;
    border-top: 1px solid #3c3c3c;
  }

  .terminal-viewport,
  .terminal-tabs {
    min-height: 0;
  }

  .terminal-header {
    align-items: flex-start;
    flex-direction: column;
    gap: 10px;
  }

  .terminal-tabs,
  .status-text,
  .tab-btn {
    max-width: 100%;
  }

  .tab-btn {
    flex-shrink: 0;
  }

}

@media (max-width: 640px) {
  .left-sidebar {
    min-height: 32dvh;
  }

  .terminal-panel {
    min-height: 40dvh;
  }

  .right-sidebar {
    min-height: 36dvh;
  }

  .tab-btn {
    padding: 4px 8px;
  }
}
</style>
