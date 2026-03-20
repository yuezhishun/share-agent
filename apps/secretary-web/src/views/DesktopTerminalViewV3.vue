<template>
  <div class="app">
    <div class="toolbar">
      <div class="logo"><i class="fa-solid fa-terminal" /> 多终端管理器 (实时)</div>
      <div class="toolbar-actions">
        <button
          type="button"
          class="sidebar-toggle-btn"
          data-testid="toggle-left-sidebar"
          @click="toggleLeftSidebar"
        >
          <i :class="leftSidebarCollapsed ? 'fa-solid fa-chevron-right' : 'fa-solid fa-chevron-left'" />
          {{ leftSidebarCollapsed ? '显示左栏' : '隐藏左栏' }}
        </button>
        <button
          type="button"
          class="sidebar-toggle-btn"
          data-testid="toggle-right-sidebar"
          @click="toggleRightSidebar"
        >
          <i :class="rightSidebarCollapsed ? 'fa-solid fa-chevron-left' : 'fa-solid fa-chevron-right'" />
          {{ rightSidebarCollapsed ? '显示右栏' : '隐藏右栏' }}
        </button>
      </div>
    </div>

    <div
      class="main"
      :class="{
        'left-collapsed': leftSidebarCollapsed,
        'right-collapsed': rightSidebarCollapsed,
        'both-collapsed': leftSidebarCollapsed && rightSidebarCollapsed
      }"
    >
      <div v-show="!leftSidebarCollapsed" class="sidebar-column left-sidebar">
        <div class="sidebar-section node-target">
          <div class="section-header">
            <span><i class="fa-solid fa-network-wired" /> 目标节点</span>
            <span class="node-state" :class="{ offline: isSelectedNodeOnline === false }">
              {{ isSelectedNodeOnline ? 'online' : 'offline' }}
            </span>
          </div>
          <div class="node-target-body">
            <select class="node-select" :value="createNodeId" @change="onTargetNodeChange($event.target.value)">
              <option v-for="node in terminalStore.nodes" :key="node.node_id" :value="node.node_id">
                {{ formatNodeOption(node) }}
              </option>
            </select>
            <div class="node-target-meta">
              <span>{{ selectedNode?.node_name || '未选择节点' }}</span>
              <span>{{ visibleTerminalInstances.length }} instances</span>
            </div>
          </div>
        </div>

        <div class="sidebar-section sessions">
          <div class="section-header">
            <span><i class="fa-regular fa-window-maximize" /> 终端会话</span>
            <div class="section-header-actions">
              <span
                id="addTerminalIcon"
                class="add-icon"
                :title="createTerminalTitle"
                data-testid="create-button"
                @click="createInstance"
              ><i class="fa-solid fa-plus" /></span>
              <span id="refreshTerminalIcon" class="add-icon" title="刷新列表" @click="refreshTerminals"><i class="fa-solid fa-rotate" /></span>
            </div>
          </div>
          <ul id="instance-list" class="terminal-list" data-testid="instance-list">
            <li
              v-for="item in visibleTerminalInstances"
              :key="item.id"
              class="terminal-item"
              :class="{ active: item.id === terminalStore.selectedInstanceId }"
              @click="connect(item.id)"
            >
              <i class="fa-regular fa-terminal" />
              <input
                v-if="renamingInstanceId === item.id"
                :ref="setRenameInstanceInputRef"
                v-model="renameInstanceValue"
                class="terminal-rename-input"
                :data-testid="`rename-instance-input-${item.id}`"
                maxlength="60"
                placeholder="输入会话名称"
                @click.stop
                @blur="saveRenameInstance(item.id)"
                @keydown.enter.prevent.stop="saveRenameInstance(item.id)"
                @keydown.esc.prevent.stop="cancelRenameInstance"
              />
              <span
                v-else
                class="terminal-name"
                :title="formatInstanceTooltip(item)"
              >{{ formatInstanceDisplayName(item) }}</span>
              <span v-if="item.node_online === false" class="instance-state offline">offline</span>
              <span class="terminal-actions">
                <span
                  class="sync-btn"
                  title="同步终端内容"
                  @click.stop="syncTerminalItem(item.id)"
                ><i class="fa-solid fa-rotate" /></span>
                <span
                  class="rename-btn"
                  :title="terminalStore.getInstanceAlias(item.id) ? '修改会话名' : '设置会话名'"
                  :data-testid="`rename-instance-${item.id}`"
                  @click.stop="beginRenameInstance(item)"
                ><i class="fa-regular fa-pen-to-square" /></span>
                <span class="close-btn" title="关闭" @click.stop="closeTerminal(item.id)"><i class="fa-regular fa-circle-xmark" /></span>
              </span>
            </li>
          </ul>
        </div>
      </div>

      <div class="terminal-panel">
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
              v-for="tab in fileTabs"
              :key="tab.id"
              type="button"
              class="tab-btn file-tab"
              :class="{ active: activeCenterTab === tab.id }"
              @click="switchCenterTab(tab.id)"
              :title="tab.path"
            >
              <i class="fa-regular fa-file" />
              <span class="tab-text">{{ tab.name }}</span>
              <i class="fa-solid fa-xmark close-icon" @click.stop="closeFileTab(tab.id)" />
            </button>
          </div>
          <div class="status-text"><span data-testid="status"><i class="fa-regular fa-keyboard" /> {{ terminalStore.status }}</span></div>
        </div>

        <div v-show="activeCenterTab === 'terminal'" class="terminal-viewport">
          <div id="terminalContent" ref="terminalRef" class="terminal-host" data-testid="terminal" />
        </div>

        <div v-if="activeFileTab" class="editor-viewport">
          <div class="editor-toolbar">
            <span class="editor-path">{{ activeFileTab.path }}</span>
            <div class="editor-actions">
              <span v-if="activeFileTab.dirty" class="dirty-indicator">未保存</span>
              <button type="button" @click="reloadFileTab(activeFileTab)" :disabled="activeFileTab.loading">重载</button>
              <button type="button" class="primary" @click="saveActiveFileTab" :disabled="activeFileTab.loading">保存</button>
            </div>
          </div>

          <div v-if="activeFileTab.error" class="panel-error">{{ activeFileTab.error }}</div>
          <div v-else-if="activeFileTab.loading" class="editor-loading">加载中...</div>
          <template v-else>
            <div v-if="activeFileTab.truncated" class="editor-warning">文件内容已截断展示（{{ activeFileTab.truncateReason || 'max_lines' }}）</div>
            <textarea
              v-model="activeFileTab.content"
              class="editor-textarea"
              spellcheck="false"
              @input="onActiveFileInput"
            />
          </template>
        </div>
      </div>

      <div v-show="!rightSidebarCollapsed" class="sidebar-column right-sidebar">
        <div class="sidebar-section file-browser">
          <div class="section-header">
            <span>
              <i :class="rightTabIconClass" />
              {{ rightTabTitle }}
            </span>
            <div v-if="activeRightTab === 'files'" class="section-header-actions">
              <span
                id="toggleHiddenFilesIcon"
                class="add-icon"
                :class="{ active: filesStore.showHidden }"
                :title="filesStore.showHidden ? '隐藏隐藏文件' : '显示隐藏文件'"
                :aria-pressed="filesStore.showHidden"
                data-testid="toggle-hidden-files"
                @click="toggleShowHidden"
              >
                <i :class="filesStore.showHidden ? 'fa-regular fa-eye-slash' : 'fa-regular fa-eye'" />
              </span>
              <span
                id="createFolderIcon"
                class="add-icon"
                :class="{ active: showFolderCreator }"
                :title="showFolderCreator ? '收起新建文件夹' : '新建文件夹'"
                data-testid="create-folder-toggle"
                @click="toggleFolderCreator"
              >
                <i class="fa-regular fa-folder-open" />
                <i class="fa-solid fa-plus overlay-plus-icon" />
              </span>
              <span
                id="uploadFileIcon"
                class="add-icon"
                title="上传文件"
                data-testid="upload-file-trigger"
                @click="pickUploadFiles"
              >
                <i class="fa-solid fa-upload" />
              </span>
            </div>
            <span
              v-else-if="activeRightTab === 'shortcuts'"
              id="addShortcutIcon"
              class="add-icon"
              :title="showShortcutEditor ? '收起快捷指令' : '新建快捷指令'"
              @click="toggleShortcutEditor"
            >
              <i :class="showShortcutEditor ? 'fa-solid fa-minus' : 'fa-solid fa-plus'" />
            </span>
            <span
              v-else
              id="addRecipeIcon"
              class="add-icon"
              :title="showRecipeEditor ? '收起配方编辑器' : '新建配方'"
              @click="showRecipeEditor ? cancelRecipeEdit() : addNewRecipe()"
            >
              <i :class="showRecipeEditor ? 'fa-solid fa-minus' : 'fa-solid fa-plus'" />
            </span>
          </div>

          <div v-if="activeRightTab === 'files'" class="file-browser-panel">
            <div class="file-header">
              <span id="currentPath" class="path-breadcrumb" :title="filesStore.currentPath">{{ currentPathDisplay }}</span>
            </div>
            <form
              v-if="showFolderCreator"
              class="folder-creator"
              data-testid="folder-creator"
              @submit.prevent="createFolder"
            >
              <input
                v-model="folderName"
                class="folder-creator-input"
                data-testid="folder-name-input"
                type="text"
                maxlength="120"
                placeholder="输入文件夹名称"
              />
              <div class="folder-creator-actions">
                <button type="submit" class="primary" :disabled="filesStore.actionLoading">创建</button>
                <button type="button" :disabled="filesStore.actionLoading" @click="toggleFolderCreator">取消</button>
              </div>
            </form>
            <ul id="fileList" class="file-list">
              <li v-if="filesStore.parentPath" class="file-item" @click="goParentDir">
                <i class="fa-regular fa-folder-open folder-icon" />
                <span class="file-name">..</span>
                <span class="file-size" />
              </li>
              <li
                v-for="item in filesStore.items"
                :key="item.path"
                class="file-item"
                :title="formatFileEntryTooltip(item)"
                @click="openFileEntry(item)"
              >
                <i :class="item.kind === 'dir' ? 'fa-regular fa-folder-open folder-icon' : 'fa-regular fa-file file-icon'" />
                <span class="file-name">{{ item.name }}</span>
                <span class="file-size">{{ item.kind === 'dir' ? '' : formatSize(item.size) }}</span>
                <span v-if="item.kind === 'file' || item.kind === 'dir'" class="file-actions">
                  <button
                    type="button"
                    class="file-action-btn"
                    :title="item.kind === 'dir' ? '下载压缩包' : '下载文件'"
                    @click.stop="downloadFileEntry(item)"
                  >
                    <i class="fa-solid fa-download" />
                  </button>
                </span>
              </li>
            </ul>
            <div v-if="filesStore.error" class="panel-error">{{ filesStore.error }}</div>
            <div v-if="filesStore.actionError" class="panel-error">{{ filesStore.actionError }}</div>
          </div>

          <div v-else-if="activeRightTab === 'shortcuts'" class="shortcut-panel">
            <form
              v-if="showShortcutEditor"
              class="shortcut-editor"
              data-testid="shortcut-editor"
              @submit.prevent="addShortcutCommand"
            >
              <div class="shortcut-editor-field">
                <span class="shortcut-editor-label">按钮名</span>
                <input
                  ref="shortcutLabelInputRef"
                  v-model="shortcutEditor.label"
                  type="text"
                  placeholder="如 tail日志"
                />
              </div>
              <div class="shortcut-editor-field">
                <span class="shortcut-editor-label">分组</span>
                <input v-model="shortcutEditor.group" type="text" placeholder="如 custom" />
              </div>
              <div class="shortcut-editor-field shortcut-editor-field-wide">
                <span class="shortcut-editor-label">指令内容</span>
                <input v-model="shortcutEditor.command" type="text" placeholder="如 tail -f app.log" />
              </div>
              <div class="shortcut-editor-actions">
                <button
                  type="button"
                  class="shortcut-enter-toggle"
                  :class="{ active: shortcutEditor.pressEnter }"
                  :title="shortcutEditor.pressEnter ? '发送后自动回车：已开启' : '发送后自动回车：已关闭'"
                  :aria-pressed="shortcutEditor.pressEnter"
                  @click="shortcutEditor.pressEnter = !shortcutEditor.pressEnter"
                >
                  <i class="fa-solid fa-reply shortcut-enter-icon" aria-hidden="true" />
                  <span>回车</span>
                </button>
                <button type="submit" class="primary">添加指令</button>
                <button type="button" @click="collapseShortcutEditor">取消</button>
              </div>
            </form>
            <div class="shortcut-note">按分组发送控制键或命令，常用新增项收在右上角 `+`。</div>
            <div class="shortcut-groups">
              <div v-for="group in shortcutGroups" :key="group.group" class="shortcut-group">
                <div class="shortcut-group-title">{{ group.group }}</div>
                <div class="shortcut-grid">
                  <button
                    v-for="item in group.items"
                    :key="item.id"
                    type="button"
                    class="shortcut-btn"
                    @click="sendShortcut(item)"
                  >
                    {{ item.label }}
                  </button>
                </div>
              </div>
            </div>
          </div>

          <div v-else class="recipes-panel">
            <form v-if="showRecipeEditor" class="recipe-editor" @submit.prevent="submitRecipeEditor">
              <input v-model="recipeEditor.name" type="text" placeholder="显示名（可选）" />
              <select v-model="recipeEditor.cwd" :disabled="recipeFoldersLoading || recipeFolderOptions.length === 0">
                <option v-for="item in recipeFolderOptions" :key="item.path" :value="item.path">
                  {{ item.label }}
                </option>
              </select>
              <textarea
                v-model="recipeEditor.commandLine"
                rows="3"
                placeholder='命令行，如 bash -lc "npm run dev" 或 ["bash","-lc","npm run dev"]'
                required
              />
              <div v-if="recipeFoldersError" class="panel-error">{{ recipeFoldersError }}</div>
              <textarea v-model="recipeEditor.envInput" rows="3" placeholder='环境变量(JSON对象)，如 {"TERM":"xterm-256color"}' />
              <div class="recipe-editor-actions">
                <button type="submit" class="primary" :disabled="recipeFoldersLoading || recipeFolderOptions.length === 0">{{ editingRecipeId ? '更新配方' : '保存配方' }}</button>
                <button type="button" @click="cancelRecipeEdit">取消</button>
              </div>
            </form>

            <ul id="recipeList" class="recipe-list">
              <li v-for="item in recipeItems" :key="item.id" class="recipe-item" :class="{ default: isDefaultCreateRecipe(item.id) }">
                <span class="recipe-icon"><i class="fa-regular fa-file-lines" /></span>
                <div class="recipe-info">
                  <div class="recipe-name">{{ item.name || item.command }}</div>
                  <div class="recipe-command" :title="formatRecipeSummary(item)">{{ formatRecipeSummary(item) }}</div>
                </div>
                <div class="recipe-actions">
                  <i
                    :class="isDefaultCreateRecipe(item.id) ? 'fa-solid fa-star' : 'fa-regular fa-star'"
                    :title="isDefaultCreateRecipe(item.id) ? '取消 + 号默认配方' : '设为 + 号默认配方'"
                    @click="toggleDefaultCreateRecipe(item)"
                  />
                  <i class="fa-regular fa-play" title="执行配方" @click="runRecipe(item)" />
                  <i class="fa-regular fa-pen-to-square" title="编辑配方" @click="editRecipe(item)" />
                  <i class="fa-regular fa-trash-can" title="删除配方" @click="removeRecipe(item.id)" />
                </div>
              </li>
            </ul>
          </div>

          <div class="right-tab-footer">
            <button
              type="button"
              class="right-tab-btn"
              :class="{ active: activeRightTab === 'files' }"
              @click="switchRightTab('files')"
            >
              文件浏览器
            </button>
            <button
              type="button"
              class="right-tab-btn"
              :class="{ active: activeRightTab === 'shortcuts' }"
              @click="switchRightTab('shortcuts')"
            >
              快捷指令
            </button>
            <button
              type="button"
              class="right-tab-btn"
              :class="{ active: activeRightTab === 'recipes' }"
              @click="switchRightTab('recipes')"
            >
              终端配方
            </button>
          </div>
        </div>
      </div>
    </div>

    <input ref="uploadFilesInputRef" class="hidden-input" type="file" multiple @change="onUploadFilesChange" />
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
  </div>
</template>

<script setup>
import { computed, nextTick, onBeforeUnmount, onMounted, ref } from 'vue';
import { Terminal } from 'xterm';
import { FitAddon } from '@xterm/addon-fit';
import 'xterm/css/xterm.css';
import { useWebCliTerminalStoreV2 } from '../stores/webcli-terminal-v2.js';
import { useWebCliFilesStore } from '../stores/webcli-files.js';
import { useWebCliRecipesStore } from '../stores/webcli-recipes.js';
import { createTerminalProtocolRendererV2 } from '../composables/useTerminalProtocolV2.js';
import {
  isTerminalGeometryChanged,
  isTerminalViewportRenderable,
  measureStableTerminalGeometry,
  normalizeTerminalGeometry
} from './desktop-terminal-resize.js';
import { formatCommandLine, parseCommandLine } from '../utils/command-line.js';

const terminalStore = useWebCliTerminalStoreV2();
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
const fileTabs = ref([]);
const editingRecipeId = ref('');
const showRecipeEditor = ref(false);
const recipeEditor = ref(buildRecipeEditor());
const defaultCreateRecipeId = ref('');
const leftSidebarCollapsed = ref(false);
const rightSidebarCollapsed = ref(false);
const customShortcutItems = ref([]);
const showShortcutEditor = ref(false);
const showFolderCreator = ref(false);
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

let term = null;
let fitAddon = null;
let unsubscribe = null;
let renderer = null;
let terminalResizeObserver = null;
let suppressRemoteResize = false;
let terminalResizeDebounceTimer = null;
let lastMeasuredGeometry = null;
let sidebarToggleDebounceTimer = null;
const defaultCwdPath = '/home/yueyuan';

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

const activeFileTab = computed(() => {
  if (activeCenterTab.value === 'terminal') {
    return null;
  }
  return fileTabs.value.find((tab) => tab.id === activeCenterTab.value) || null;
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

const defaultRecipeStorageKey = 'webcli-default-create-recipe-v1';
const customShortcutStorageKey = 'webcli-shortcuts-v1';
const codexQuickCommand = 'codex --dangerously-bypass-approvals-and-sandbox';
const comboKeyIntervalMs = 120;
const quickCommandIntervalMs = 300;
const mobileAutoCollapseWidth = 820;

const builtInShortcutItems = [
  { id: 'esc', label: 'esc', group: '控制键', value: '\u001b' },
  { id: 'tab', label: 'tab', group: '控制键', value: '\t' },
  { id: 'enter', label: 'enter', group: '控制键', value: '\r' },
  { id: 'altEnter', label: 'alt+enter', group: '控制键', value: '\u001b\r' },
  { id: 'backspace', label: 'backspace', group: '控制键', value: '\u007f' },
  { id: 'delete', label: 'delete', group: '控制键', value: '\u001b[3~' },
  { id: 'ctrlC', label: 'ctrl+c', group: '控制键', value: '\u0003' },
  { id: 'altTab', label: 'alt+tab', group: '控制键', value: '\u001b[Z' },
  { id: 'codex', label: 'codex', group: '常用命令', sequence: [codexQuickCommand, '\r'], intervalMs: quickCommandIntervalMs },
  { id: 'home', label: 'home', group: '导航键', value: '\u001b[H' },
  { id: 'end', label: 'end', group: '导航键', value: '\u001b[F' },
  { id: 'arrowLeft', label: '←', group: '导航键', value: '\u001b[D' },
  { id: 'arrowUp', label: '↑', group: '导航键', value: '\u001b[A' },
  { id: 'arrowDown', label: '↓', group: '导航键', value: '\u001b[B' },
  { id: 'arrowRight', label: '→', group: '导航键', value: '\u001b[C' },
  { id: 'pgUp', label: 'pgUp', group: '导航键', value: '\u001b[5~' },
  { id: 'pgDn', label: 'pgDn', group: '导航键', value: '\u001b[6~' },
  { id: 'at', label: '@', group: '常用命令', value: '@' },
  { id: 'bang', label: '!', group: '常用命令', value: '!' },
  { id: 'slash', label: '/', group: '常用命令', value: '/' },
  { id: 'ls', label: 'ls', group: '常用命令', sequence: ['ls', '\r'], intervalMs: quickCommandIntervalMs },
  { id: 'pwd', label: 'pwd', group: '常用命令', sequence: ['pwd', '\r'], intervalMs: quickCommandIntervalMs },
  { id: 'resume', label: '/resume', group: '常用命令', sequence: ['/resume', '\r'], intervalMs: quickCommandIntervalMs },
  { id: 'new', label: '/new', group: '常用命令', sequence: ['/new', '\r'], intervalMs: quickCommandIntervalMs },
  { id: 'status', label: '/status', group: '常用命令', sequence: ['/status', '\r'], intervalMs: quickCommandIntervalMs }
];

const shortcutGroupOrder = ['控制键', '导航键', '常用命令', 'custom', '自定义'];

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

const shortcutItems = computed(() => [...builtInShortcutItems, ...customShortcutItems.value]);

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

function buildRecipeEditor(seed = null) {
  const source = seed || {};
  return {
    name: String(source.name || ''),
    cwd: normalizeSelectableCwd(source.cwd),
    commandLine: formatCommandLine(
      String(source.command || 'bash') || 'bash',
      Array.isArray(source.args) ? source.args : ['-i']
    ),
    envInput: JSON.stringify(source.env && typeof source.env === 'object' && !Array.isArray(source.env) ? source.env : {}, null, 2),
    group: String(source.group || 'general')
  };
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

function parseJsonOrDefault(input, fallback) {
  const text = String(input || '').trim();
  if (!text) {
    return fallback;
  }
  return JSON.parse(text);
}

function parseRecipeEnv(input) {
  const raw = String(input || '').trim();
  if (!raw) {
    return {};
  }
  const parsed = JSON.parse(raw);
  if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
    throw new Error('环境变量必须是 JSON 对象');
  }
  return parsed;
}

function normalizeSelectableCwd(value) {
  const text = String(value || '').trim();
  if (!text) {
    return defaultCwdPath;
  }
  return recipeFolderOptions.value.some((item) => item.path === text) ? text : defaultCwdPath;
}

async function loadRecipeFolders(nodeId = createNodeId.value) {
  const targetNodeId = String(nodeId || '').trim();
  recipeFoldersLoading.value = true;
  recipeFoldersError.value = '';
  try {
    const response = await fetch(buildNodeFileApiPath(targetNodeId, '/list', {
      path: defaultCwdPath,
      show_hidden: filesStore.showHidden ? '1' : '0'
    }));
    if (!response.ok) {
      throw new Error(await parseErrorMessage(response, `load recipe folders failed: ${response.status}`));
    }

    const payload = await response.json();
    const items = Array.isArray(payload?.items) ? payload.items : [];
    recipeFolderOptions.value = items
      .filter((item) => item?.kind === 'dir')
      .map((item) => ({
        path: String(item.path || '').trim(),
        label: String(item.name || item.path || '').trim() || String(item.path || '').trim()
      }))
      .filter((item) => item.path);

    if (!recipeFolderOptions.value.some((item) => item.path === defaultCwdPath)) {
      recipeFolderOptions.value.unshift({
        path: defaultCwdPath,
        label: defaultCwdPath.split('/').filter(Boolean).pop() || defaultCwdPath
      });
    }
    recipeEditor.value.cwd = normalizeSelectableCwd(recipeEditor.value.cwd);
  } catch (error) {
    recipeFoldersError.value = String(error?.message || error || 'load recipe folders failed');
    recipeFolderOptions.value = [{
      path: defaultCwdPath,
      label: defaultCwdPath.split('/').filter(Boolean).pop() || defaultCwdPath
    }];
    recipeEditor.value.cwd = defaultCwdPath;
  } finally {
    recipeFoldersLoading.value = false;
  }
}

function compressPath(path) {
  const raw = String(path || '').trim();
  if (!raw) {
    return '/';
  }

  const normalized = raw.replace(/\/+/g, '/');
  const segments = normalized.split('/').filter(Boolean);
  if (segments.length <= 3) {
    return normalized;
  }

  return `.../${segments.slice(-3).join('/')}`;
}

function normalizeShortcutGroup(value) {
  const text = String(value || '').trim();
  return text || 'custom';
}

function compareShortcutGroup(a, b) {
  const left = normalizeShortcutGroup(a);
  const right = normalizeShortcutGroup(b);
  const leftIndex = shortcutGroupOrder.indexOf(left);
  const rightIndex = shortcutGroupOrder.indexOf(right);
  if (leftIndex >= 0 || rightIndex >= 0) {
    if (leftIndex < 0) {
      return 1;
    }
    if (rightIndex < 0) {
      return -1;
    }
    if (leftIndex !== rightIndex) {
      return leftIndex - rightIndex;
    }
  }
  return left.localeCompare(right, 'zh-Hans-CN');
}

function toShortcutPayload(item) {
  const id = String(item?.id || '').trim();
  const label = String(item?.label || '').trim();
  const group = normalizeShortcutGroup(item?.group);
  const sequence = Array.isArray(item?.sequence)
    ? item.sequence.map((x) => String(x ?? '')).filter((x) => x.length > 0)
    : [];
  const value = String(item?.value || '').trim();
  const intervalMs = Number(item?.intervalMs);

  if (!id || !label) {
    return null;
  }

  return {
    id,
    label,
    group,
    sequence,
    value,
    intervalMs: Number.isFinite(intervalMs) ? Math.max(0, Math.floor(intervalMs)) : quickCommandIntervalMs
  };
}

function getActiveNodeId() {
  return String(createNodeId.value || terminalStore.selectedInstance?.node_id || terminalStore.getDefaultNodeId() || '').trim();
}

function hydrateCustomShortcuts() {
  try {
    const raw = localStorage.getItem(customShortcutStorageKey);
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
    localStorage.setItem(customShortcutStorageKey, JSON.stringify({ items: payload }));
  } catch {
  }
}

function hydrateDefaultCreateRecipe() {
  try {
    const value = localStorage.getItem(defaultRecipeStorageKey);
    defaultCreateRecipeId.value = String(value || '').trim();
  } catch {
    defaultCreateRecipeId.value = '';
  }
}

function persistDefaultCreateRecipe() {
  const id = String(defaultCreateRecipeId.value || '').trim();
  try {
    if (!id) {
      localStorage.removeItem(defaultRecipeStorageKey);
      return;
    }
    localStorage.setItem(defaultRecipeStorageKey, id);
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
    intervalMs: quickCommandIntervalMs
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
  term?.focus();
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
  if (!isTerminalViewportRenderable(activeCenterTab.value, terminalRef.value)) {
    return;
  }

  fitAddon?.fit();
  lastMeasuredGeometry = normalizeTerminalGeometry(term?.cols, term?.rows);
}

async function ensureMeasuredTerminalReady() {
  await nextTick();
  const fontsReady = typeof document !== 'undefined' && document.fonts?.ready && typeof document.fonts.ready.then === 'function'
    ? Promise.race([
        document.fonts.ready.catch(() => {}),
        wait(700)
      ])
    : Promise.resolve();
  await fontsReady;

  const measured = await measureStableTerminalGeometry({
    activeCenterTab: activeCenterTab.value,
    hostElement: terminalRef.value,
    fitAddon,
    term,
    isDocumentHidden: () => typeof document !== 'undefined' && document.hidden === true,
    wait
  });
  return measured;
}

function handleTerminalResize({ cols: c, rows: r }) {
  if (suppressRemoteResize || document.hidden || !isTerminalViewportRenderable(activeCenterTab.value, terminalRef.value)) {
    return;
  }

  if (!isTerminalGeometryChanged(lastMeasuredGeometry, { cols: c, rows: r })) {
    return;
  }

  lastMeasuredGeometry = normalizeTerminalGeometry(c, r);
  terminalStore.sendResize(c, r);
}

function isMobileViewport() {
  if (typeof window === 'undefined') {
    return false;
  }
  return window.innerWidth <= mobileAutoCollapseWidth;
}

function applyResponsiveSidebarCollapse() {
  if (!isMobileViewport()) {
    return;
  }
  if (!leftSidebarCollapsed.value) {
    leftSidebarCollapsed.value = true;
  }
  if (!rightSidebarCollapsed.value) {
    rightSidebarCollapsed.value = true;
  }
}

function onWindowResize() {
  applyResponsiveSidebarCollapse();
  fitTerminal();
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
  applyResponsiveSidebarCollapse();
  fitTerminal();
  if (typeof document !== 'undefined' && document.hidden === false) {
    syncVisibleTerminal('visibility');
  }
}

async function fitAfterFontsReady() {
  if (typeof document === 'undefined') {
    return;
  }
  const fonts = document.fonts;
  if (!fonts?.ready || typeof fonts.ready.then !== 'function') {
    return;
  }
  try {
    await fonts.ready;
    await fitTerminal();
  } catch {
  }
}

async function toggleLeftSidebar() {
  leftSidebarCollapsed.value = !leftSidebarCollapsed.value;
  await scheduleSidebarLayoutSync('left_sidebar');
  if (activeCenterTab.value === 'terminal') {
    focusTerminal();
  }
}

async function toggleRightSidebar() {
  rightSidebarCollapsed.value = !rightSidebarCollapsed.value;
  await scheduleSidebarLayoutSync('right_sidebar');
  if (activeCenterTab.value === 'terminal') {
    focusTerminal();
  }
}

async function scheduleSidebarLayoutSync(reason = 'sidebar_toggle') {
  if (sidebarToggleDebounceTimer) {
    clearTimeout(sidebarToggleDebounceTimer);
    sidebarToggleDebounceTimer = null;
  }

  await new Promise((resolve) => {
    sidebarToggleDebounceTimer = window.setTimeout(async () => {
      sidebarToggleDebounceTimer = null;
      const geometry = await ensureMeasuredTerminalReady();
      if (!geometry || !isTerminalGeometryChanged(lastMeasuredGeometry, geometry)) {
        await fitTerminal();
        resolve();
        return;
      }

      lastMeasuredGeometry = normalizeTerminalGeometry(geometry.cols, geometry.rows);
      suppressRemoteResize = true;
      try {
        fitAddon?.fit();
      } finally {
        suppressRemoteResize = false;
      }

      if (terminalStore.wsConnected && terminalStore.selectedInstanceId && activeCenterTab.value === 'terminal') {
        terminalStore.sendResize(geometry.cols, geometry.rows);
        terminalStore.resync().catch(() => {
          terminalStore.setStatus(`screen sync failed: ${reason}`);
        });
      }
      resolve();
    }, 140);
  });
}

function switchCenterTab(tabId) {
  activeCenterTab.value = tabId;
  if (tabId === 'terminal') {
    nextTick(() => {
      fitTerminal();
      focusTerminal();
      syncVisibleTerminal('tab_switch');
    });
  }
}

function switchRightTab(tabId) {
  const target = ['files', 'shortcuts', 'recipes'].includes(tabId) ? tabId : 'files';
  terminalStore.setUiSession({ activeRightTab: target });
}

function formatNodeOption(node) {
  if (!node) {
    return '未知节点';
  }
  const name = String(node.node_name || node.node_id || 'node').trim();
  const role = String(node.node_role || '').trim();
  const status = node.node_online === false ? 'offline' : 'online';
  return role ? `${name} · ${role} · ${status}` : `${name} · ${status}`;
}

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

function formatInstanceSummary(instance) {
  const instanceCwd = String(instance?.cwd || '').trim() || '~';
  const instanceCommand = String(instance?.command || '').trim() || 'bash';
  return `${instanceCommand} · ${compressPath(instanceCwd)}`;
}

function formatInstanceTooltip(instance) {
  const instanceAlias = terminalStore.getInstanceAlias(instance?.id);
  const instanceCwd = String(instance?.cwd || '').trim() || '~';
  const instanceCommand = String(instance?.command || '').trim() || 'bash';
  const summary = `${instanceCommand}\n${instanceCwd}`;
  if (!instanceAlias) {
    return summary;
  }
  return `${instanceAlias}\n${summary}\n${String(instance?.id || '').trim()}`;
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

function formatRecipeSummary(item) {
  const recipeCwd = String(item?.cwd || '').trim() || '~';
  const recipeCommand = String(item?.command || '').trim() || 'bash';
  const recipeArgs = Array.isArray(item?.args) && item.args.length > 0 ? ` ${item.args.join(' ')}` : '';
  return `${recipeCwd} | ${recipeCommand}${recipeArgs}`;
}

function formatSize(size) {
  const value = Number(size || 0);
  if (!Number.isFinite(value) || value <= 0) {
    return '0 B';
  }
  if (value < 1024) {
    return `${value} B`;
  }
  if (value < 1024 * 1024) {
    return `${(value / 1024).toFixed(1)} KB`;
  }
  if (value < 1024 * 1024 * 1024) {
    return `${(value / (1024 * 1024)).toFixed(1)} MB`;
  }
  return `${(value / (1024 * 1024 * 1024)).toFixed(1)} GB`;
}

function formatFileModifiedTime(value) {
  if (!value) {
    return '未知';
  }
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return String(value);
  }
  return new Intl.DateTimeFormat('zh-CN', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit'
  }).format(date);
}

function formatFileEntryTooltip(item) {
  const name = String(item?.name || item?.path || '').trim() || '未命名';
  const modified = formatFileModifiedTime(item?.mtime);
  return `${name}\n最后修改：${modified}`;
}

async function loadFilesForSelected() {
  const nodeId = getActiveNodeId();
  const path = terminalStore.selectedInstance?.cwd || filesStore.currentPath || filesStore.basePath;
  await filesStore.loadList(path, nodeId);
}

async function connect(instanceId) {
  const id = String(instanceId || '').trim();
  if (!id) {
    return;
  }
  try {
    const geometry = await ensureMeasuredTerminalReady();
    if (!geometry) {
      terminalStore.setStatus('Terminal viewport is not ready');
      return;
    }

    await terminalStore.connect(id);
    terminalStore.sendResize(geometry.cols, geometry.rows);
    await fitTerminal();
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
      terminalStore.fetchInstances(),
      terminalStore.fetchNodes()
    ]);

    if (!createNodeId.value || !terminalStore.nodes.some((item) => String(item?.node_id || '').trim() === String(createNodeId.value || '').trim())) {
      createNodeId.value = String(terminalStore.selectedInstance?.node_id || terminalStore.getDefaultNodeId() || '');
    }
    await loadRecipeFolders(createNodeId.value);

    if (terminalStore.instances.length === 0) {
      terminalStore.disconnect();
      terminalStore.selectedInstanceId = '';
      await filesStore.loadList(defaultCwdPath, getActiveNodeId());
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
    await filesStore.loadList(defaultCwdPath, targetId);
    terminalStore.setStatus(targetId ? '当前节点暂无终端会话' : '暂无终端会话');
    return;
  }

  const selectedVisible = visibleItems.some((item) => item.id === terminalStore.selectedInstanceId);
  const nextId = selectedVisible ? terminalStore.selectedInstanceId : visibleItems[0].id;
  await connect(nextId);
}

async function createInstance() {
  try {
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
    const fallbackCwd = terminalStore.selectedInstance?.cwd || filesStore.currentPath || filesStore.basePath || defaultCwdPath;
    const resolvedCwd = selectedRecipe
      ? normalizeSelectableCwd(selectedRecipe.cwd || fallbackCwd)
      : normalizeSelectableCwd(cwd.value || fallbackCwd);
    const nodeId = getActiveNodeId();

    const created = await terminalStore.createInstance({
      command: commandText,
      args: parsedArgs,
      env: parsedEnv,
      cols: Number(cols.value) || 120,
      rows: Number(rows.value) || 34,
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
      await filesStore.loadList(defaultCwdPath, getActiveNodeId());
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

async function openFileDocument(path, nodeId = getActiveNodeId()) {
  const response = await fetch(buildNodeFileApiPath(nodeId, '/read', {
    path: String(path || ''),
    max_lines: '2000'
  }));
  if (!response.ok) {
    throw new Error(await parseErrorMessage(response, `read failed: ${response.status}`));
  }
  return response.json();
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

  const tab = {
    id: tabId,
    nodeId: normalizedNodeId,
    path: normalizedPath,
    name: displayName || normalizedPath.split('/').pop() || normalizedPath,
    loading: true,
    error: '',
    content: '',
    lastSavedContent: '',
    dirty: false,
    truncated: false,
    truncateReason: ''
  };
  fileTabs.value = [...fileTabs.value, tab];
  switchCenterTab(tabId);

  try {
    const payload = await openFileDocument(normalizedPath, normalizedNodeId);
    const content = String(payload?.content || '');
    patchFileTab(tabId, {
      content,
      lastSavedContent: content,
      dirty: false,
      error: '',
      truncated: payload?.truncated === true,
      truncateReason: String(payload?.truncate_reason || '')
    });
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
  const next = fileTabs.value.filter((x) => x.id !== id);
  fileTabs.value = next;
  if (activeCenterTab.value === id) {
    switchCenterTab('terminal');
  }
}

function onActiveFileInput() {
  const tab = activeFileTab.value;
  if (!tab) {
    return;
  }
  tab.dirty = tab.content !== tab.lastSavedContent;
}

async function saveActiveFileTab() {
  const tab = activeFileTab.value;
  if (!tab || tab.loading) {
    return;
  }

  patchFileTab(tab.id, { error: '' });
  try {
    await filesStore.saveFile(tab.path, tab.content, tab.nodeId);
    patchFileTab(tab.id, {
      lastSavedContent: tab.content,
      dirty: false
    });
    terminalStore.setStatus(`Saved: ${tab.path}`);
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
  if (tab.dirty && !window.confirm('当前文件有未保存修改，确认重载并丢弃修改？')) {
    return;
  }

  patchFileTab(tab.id, {
    loading: true,
    error: ''
  });
  try {
    const payload = await openFileDocument(tab.path, tab.nodeId);
    const content = String(payload?.content || '');
    patchFileTab(tab.id, {
      content,
      lastSavedContent: content,
      dirty: false,
      truncated: payload?.truncated === true,
      truncateReason: String(payload?.truncate_reason || '')
    });
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
    const args = Array.isArray(item?.args) ? item.args.map((x) => String(x)) : [];
    const env = item?.env && typeof item.env === 'object' && !Array.isArray(item.env) ? item.env : {};
    const recipeCommand = String(item?.command || '').trim() || 'bash';
    const fallbackCwd = terminalStore.selectedInstance?.cwd || filesStore.currentPath || filesStore.basePath || defaultCwdPath;
    const recipeCwd = normalizeSelectableCwd(item?.cwd || fallbackCwd);
    const nodeId = getActiveNodeId();

    const created = await terminalStore.createInstance({
      command: recipeCommand,
      args,
      env,
      cols: Number(cols.value) || 120,
      rows: Number(rows.value) || 34,
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
    cwd: normalizeSelectableCwd(cwd.value || defaultCwdPath),
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
      cwd: normalizeSelectableCwd(cwd.value || terminalStore.selectedInstance?.cwd || filesStore.currentPath || filesStore.basePath || defaultCwdPath),
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
  const text = event?.clipboardData?.getData('text');
  if (!text) {
    return;
  }
  event.preventDefault();
  terminalStore.sendBracketedPaste(text).finally(() => {
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
    : (payloads.length > 1 ? comboKeyIntervalMs : 0);

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
  if (defaultCreateRecipeId.value && !defaultCreateRecipe.value) {
    defaultCreateRecipeId.value = '';
    persistDefaultCreateRecipe();
  }
  applyResponsiveSidebarCollapse();

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
  term.attachCustomKeyEventHandler(onTerminalKeyEvent);
  if (typeof window !== 'undefined' && typeof window.__WEBCLI_DESKTOP_TERM_HOOK__ === 'function') {
    window.__WEBCLI_DESKTOP_TERM_HOOK__(term);
  }

  renderer = createTerminalProtocolRendererV2(term);
  unsubscribe = terminalStore.subscribe((message) => {
    renderer.onMessage(message);
  });

  term.onData((data) => terminalStore.sendInput(data));
  term.onResize(handleTerminalResize);
  terminalRef.value?.addEventListener('copy', onTerminalCopy);
  terminalRef.value?.addEventListener('paste', onTerminalPaste);
  terminalRef.value?.addEventListener('contextmenu', onTerminalContextMenu);
  if (typeof ResizeObserver === 'function' && terminalRef.value) {
    terminalResizeObserver = new ResizeObserver(() => {
      if (document.hidden || !isTerminalViewportRenderable(activeCenterTab.value, terminalRef.value)) {
        return;
      }

      if (terminalResizeDebounceTimer) {
        clearTimeout(terminalResizeDebounceTimer);
      }
      terminalResizeDebounceTimer = window.setTimeout(() => {
        suppressRemoteResize = activeCenterTab.value !== 'terminal';
        try {
          fitAddon?.fit();
          lastMeasuredGeometry = normalizeTerminalGeometry(term?.cols, term?.rows);
        } finally {
          suppressRemoteResize = false;
          terminalResizeDebounceTimer = null;
        }
      }, 120);
    });
    terminalResizeObserver.observe(terminalRef.value);
  }
  window.addEventListener('resize', onWindowResize);
  document.addEventListener('visibilitychange', onDocumentVisibilityChange);

  await fitTerminal();
  fitAfterFontsReady().catch(() => {});
  await Promise.all([
    refreshTerminals()
  ]);
  if (!createNodeId.value) {
    createNodeId.value = String(terminalStore.selectedInstance?.node_id || terminalStore.getDefaultNodeId() || '');
  }
  await loadRecipeFolders(createNodeId.value);
  focusTerminal();
});

onBeforeUnmount(() => {
  unsubscribe?.();
  terminalStore.disconnect();
  window.removeEventListener('resize', onWindowResize);
  document.removeEventListener('visibilitychange', onDocumentVisibilityChange);
  terminalResizeObserver?.disconnect();
  terminalResizeObserver = null;
  if (terminalResizeDebounceTimer) {
    clearTimeout(terminalResizeDebounceTimer);
    terminalResizeDebounceTimer = null;
  }
  terminalRef.value?.removeEventListener('copy', onTerminalCopy);
  terminalRef.value?.removeEventListener('paste', onTerminalPaste);
  terminalRef.value?.removeEventListener('contextmenu', onTerminalContextMenu);
  if (sidebarToggleDebounceTimer) {
    clearTimeout(sidebarToggleDebounceTimer);
    sidebarToggleDebounceTimer = null;
  }
  term?.dispose();
});
</script>

<style scoped>
* {
  margin: 0;
  padding: 0;
  box-sizing: border-box;
}

.app {
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

.toolbar-actions {
  margin-left: auto;
  display: inline-flex;
  align-items: center;
  gap: 8px;
}

.sidebar-toggle-btn {
  border-color: #3f5b77;
  background-color: #23364a;
  color: #dbefff;
  font-size: 0.8rem;
}

.sidebar-toggle-btn:hover {
  background-color: #2a4560;
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

.sidebar-section {
  display: flex;
  flex-direction: column;
  overflow: hidden;
  border-bottom: 1px solid #3c3c3c;
}

.sidebar-section:last-child {
  border-bottom: none;
}

.node-target {
  flex: 0 0 auto;
}

.sessions {
  flex: 2;
  min-height: 0;
}

.file-browser {
  flex: 1;
  min-height: 0;
  background-color: #1e1e1e;
}

.section-header {
  padding: 10px 16px 6px;
  font-size: 0.75rem;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  color: #9e9e9e;
  display: flex;
  align-items: center;
  justify-content: space-between;
  background-color: #2a2a2a;
}

.section-header span {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.section-header-actions {
  display: inline-flex;
  align-items: center;
  gap: 4px;
}

.node-state {
  font-size: 0.7rem;
  color: #89d185;
}

.node-state.offline {
  color: #ef6a62;
}

.section-header .add-icon {
  position: relative;
  cursor: pointer;
  color: #b4b4b4;
  padding: 4px;
  border-radius: 4px;
  transition: background 0.1s;
}

.section-header .add-icon:hover {
  background-color: #3a3a3a;
  color: #fff;
}

.section-header .add-icon.active {
  background-color: rgba(14, 99, 156, 0.22);
  color: #9cdcfe;
}

.overlay-plus-icon {
  position: absolute;
  right: 1px;
  bottom: 1px;
  font-size: 0.52rem;
}

.node-target-body,
.recipes-panel {
  padding: 10px;
  display: flex;
  flex-direction: column;
  gap: 10px;
  min-height: 0;
}

.node-select {
  width: 100%;
  background: #141b24;
  border: 1px solid #3d4b5b;
  color: #e0e0e0;
  border-radius: 4px;
  padding: 8px 10px;
  font-size: 0.8rem;
}

.node-target-meta {
  color: #9eb8d4;
  font-size: 0.74rem;
}

.node-target-meta {
  display: flex;
  justify-content: space-between;
  gap: 8px;
}

.terminal-list,
.recipe-list,
.file-list {
  list-style: none;
  padding: 4px 0;
  overflow-y: auto;
}

.terminal-item {
  display: grid;
  grid-template-columns: 20px minmax(0, 1fr) auto;
  align-items: center;
  padding: 7px 12px;
  padding-right: 12px;
  margin: 2px 8px;
  border-radius: 4px;
  cursor: pointer;
  font-size: 0.84rem;
  gap: 8px;
  min-height: 36px;
  box-sizing: border-box;
  position: relative;
  transition: background-color 0.12s ease, padding-right 0.12s ease;
}

.terminal-item:hover {
  background-color: #2a2d2e;
}

.terminal-item:hover,
.terminal-item:focus-within {
  padding-right: 88px;
}

.terminal-item.active {
  background-color: #37373d;
}

.terminal-item i {
  color: #6c9cbe;
  width: 18px;
}

.terminal-name {
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  min-width: 0;
  font-family: 'JetBrains Mono', monospace;
}

.terminal-rename-input {
  width: 100%;
  min-width: 0;
  background: #141b24;
  border: 1px solid #3d4b5b;
  color: #e0e0e0;
  border-radius: 4px;
  padding: 4px 6px;
  font-size: 0.8rem;
}

.instance-state {
  font-size: 0.72rem;
  color: #97c6f2;
}

.instance-state.offline {
  color: #ef6a62;
}

.terminal-actions {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  justify-content: flex-end;
  width: 76px;
  position: absolute;
  right: 12px;
  top: 50%;
  transform: translateY(-50%);
  visibility: hidden;
  opacity: 0;
  pointer-events: none;
  transition: opacity 0.12s ease;
}

.sync-btn,
.rename-btn,
.close-btn {
  color: #9e9e9e;
  border-radius: 4px;
  padding: 2px 4px;
  font-size: 0.8rem;
}

.terminal-item:hover .terminal-actions,
.terminal-item:focus-within .terminal-actions {
  visibility: visible;
  opacity: 1;
  pointer-events: auto;
}

.sync-btn:hover,
.rename-btn:hover,
.close-btn:hover {
  background-color: #4a4a4a;
  color: #fff;
}

.recipe-editor {
  padding: 10px;
  display: flex;
  flex-direction: column;
  gap: 8px;
  border-bottom: 1px solid #3c3c3c;
  background-color: #1f2832;
}

.recipe-editor input,
.recipe-editor select,
.recipe-editor textarea {
  width: 100%;
  background: #141b24;
  border: 1px solid #3d4b5b;
  color: #e0e0e0;
  border-radius: 4px;
  padding: 6px 8px;
  font-size: 0.82rem;
}

.recipe-editor-actions {
  display: flex;
  gap: 8px;
}

.recipe-item {
  display: flex;
  align-items: center;
  padding: 8px 12px;
  margin: 2px 8px;
  border-radius: 4px;
  font-size: 0.82rem;
  gap: 8px;
}

.recipe-item:hover {
  background-color: #2a2d2e;
}

.recipe-item.default {
  border: 1px solid #3f6e98;
  background-color: #24384d;
}

.recipe-icon {
  color: #c586c0;
  width: 20px;
}

.recipe-info {
  flex: 1;
  overflow: hidden;
}

.recipe-name {
  font-weight: 500;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.recipe-command {
  font-family: 'JetBrains Mono', monospace;
  font-size: 0.7rem;
  color: #9cdcfe;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.recipe-actions {
  display: flex;
  gap: 6px;
}

.recipe-actions i {
  color: #b4b4b4;
  cursor: pointer;
  padding: 4px;
  border-radius: 4px;
  font-size: 0.8rem;
}

.recipe-actions i:hover {
  background-color: #4a4a4a;
  color: #fff;
}

.terminal-panel {
  background-color: #1e1e1e;
  display: flex;
  flex-direction: column;
  padding: 12px 14px;
  overflow: hidden;
  min-width: 0;
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
  max-width: 320px;
}

.tab-btn.active {
  background-color: #134a74;
  border-color: #0e639c;
  color: #fff;
}

.file-tab .tab-text {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.file-tab .close-icon {
  font-size: 0.7rem;
  opacity: 0.7;
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
  flex: 1;
  overflow: hidden;
  box-shadow: inset 0 0 8px rgba(0, 0, 0, 0.6);
  min-height: 320px;
  max-height: min(100%, 960px);
}

.terminal-host {
  width: 100%;
  height: 100%;
  min-height: 320px;
  max-width: 100%;
}

.editor-viewport {
  background-color: #111826;
  border-radius: 6px;
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  border: 1px solid #2f4257;
}

.editor-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  padding: 8px 10px;
  border-bottom: 1px solid #2f4257;
  background-color: #1a2735;
}

.editor-path {
  color: #9cdcfe;
  font-family: 'JetBrains Mono', monospace;
  font-size: 0.78rem;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.editor-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}

.dirty-indicator {
  color: #f0a64a;
  font-size: 0.75rem;
}

.editor-loading {
  padding: 16px;
  color: #b8c7d9;
  font-size: 0.86rem;
}

.editor-warning {
  padding: 8px 10px;
  color: #f0a64a;
  font-size: 0.78rem;
  border-bottom: 1px solid #2f4257;
  background-color: #1f2c3a;
}

.editor-textarea {
  flex: 1;
  width: 100%;
  border: none;
  outline: none;
  resize: none;
  background: transparent;
  color: #edf2f7;
  padding: 12px;
  font-family: 'JetBrains Mono', monospace;
  font-size: 0.84rem;
  line-height: 1.45;
}

.file-header {
  padding: 8px 12px;
  background-color: #2d2d2d;
  border-bottom: 1px solid #3c3c3c;
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.folder-creator {
  padding: 10px 12px;
  border-bottom: 1px solid #3c3c3c;
  display: flex;
  align-items: center;
  gap: 10px;
  background: #232a33;
}

.folder-creator-input {
  flex: 1;
  min-width: 0;
  background: #141b24;
  border: 1px solid #3d4b5b;
  color: #e0e0e0;
  border-radius: 6px;
  padding: 8px 10px;
  font-size: 0.82rem;
}

.folder-creator-actions {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  flex: 0 0 auto;
}

.file-browser-panel {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.path-breadcrumb {
  background-color: #3c3c3c;
  padding: 4px 10px;
  border-radius: 16px;
  font-size: 0.8rem;
  font-family: 'JetBrains Mono', monospace;
  color: #9cdcfe;
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.file-browser-panel .file-list {
  flex: 1;
  min-height: 0;
}

.file-item {
  display: flex;
  align-items: center;
  padding: 6px 16px;
  margin: 2px 8px;
  border-radius: 4px;
  cursor: pointer;
  font-size: 0.85rem;
  gap: 10px;
}

.file-item:hover {
  background-color: #2a2d2e;
}

.file-item i {
  width: 20px;
}

.file-actions {
  opacity: 0;
  transition: opacity 0.12s;
}

.file-item:hover .file-actions {
  opacity: 1;
}

.file-name {
  flex: 1;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.file-size {
  color: #9e9e9e;
  font-size: 0.7rem;
}

.file-action-btn {
  border: 1px solid #3f4e60;
  background-color: #2d3a4a;
  color: #d9e7f6;
  border-radius: 6px;
  padding: 2px 8px;
}

.file-action-btn i {
  width: auto;
}

.file-action-btn:hover {
  background-color: #39506a;
}

.folder-icon {
  color: #c586c0;
}

.file-icon {
  color: #9cdcfe;
}

.panel-error {
  color: #ef6a62;
  font-size: 12px;
  padding: 8px 12px;
}

.shortcut-panel {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  padding: 10px;
  display: flex;
  flex-direction: column;
  gap: 10px;
  background-color: #1f2430;
}

.recipes-panel {
  flex: 1;
  overflow-y: auto;
  background-color: #1f2832;
}

.shortcut-note {
  font-size: 0.74rem;
  color: #9eb8d4;
}

.shortcut-editor {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
  padding: 12px;
  border: 1px solid #303a47;
  border-radius: 12px;
  background: linear-gradient(180deg, rgba(25, 33, 43, 0.96), rgba(19, 25, 33, 0.96));
}

.shortcut-editor-field {
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-width: 0;
}

.shortcut-editor-field-wide {
  grid-column: 1 / -1;
}

.shortcut-editor-label {
  font-size: 0.72rem;
  color: #8faecc;
  letter-spacing: 0.02em;
}

.shortcut-editor input[type='text'] {
  width: 100%;
  background: #141b24;
  border: 1px solid #3d4b5b;
  color: #e0e0e0;
  border-radius: 4px;
  padding: 6px 8px;
  font-size: 0.76rem;
  min-height: 34px;
}

.shortcut-editor-actions {
  grid-column: 1 / -1;
  display: flex;
  align-items: center;
  justify-content: flex-start;
  gap: 10px;
  padding-top: 2px;
  flex-wrap: wrap;
}

.shortcut-editor .shortcut-enter-toggle {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: auto;
  min-width: 88px;
  height: 36px;
  border: 1px solid #3d4b5b;
  border-radius: 10px;
  background: #141b24;
  color: #9eb8d4;
  cursor: pointer;
  transition: background-color 0.15s ease, border-color 0.15s ease, color 0.15s ease;
  gap: 8px;
  padding: 0 12px;
}

.shortcut-editor .shortcut-enter-toggle:hover {
  background-color: #1a2430;
  border-color: #53708f;
}

.shortcut-editor .shortcut-enter-toggle.active {
  background-color: rgba(14, 99, 156, 0.22);
  border-color: #0e639c;
  color: #9cdcfe;
}

.shortcut-enter-icon {
  font-size: 0.95rem;
}

.shortcut-editor .primary {
  min-width: 104px;
  justify-content: center;
}

.shortcut-groups {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.shortcut-group {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.shortcut-group-title {
  font-size: 0.72rem;
  color: #95b5d8;
  text-transform: uppercase;
  letter-spacing: 0.4px;
}

.shortcut-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 8px;
}

.shortcut-btn {
  justify-content: center;
  font-size: 0.76rem;
  background-color: #2d3a4a;
  border-color: #3f4e60;
  color: #d9e7f6;
}

.shortcut-btn:hover {
  background-color: #39506a;
}

.right-tab-footer {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 8px;
  padding: 8px 10px 10px;
  border-top: 1px solid #3c3c3c;
  background-color: #252526;
}

.right-tab-btn {
  justify-content: center;
  font-size: 0.78rem;
  padding: 6px 8px;
}

.right-tab-btn.active {
  background-color: #0e639c;
  color: #fff;
  border-color: #0e639c;
}

.hidden-input,
.test-hook {
  display: none;
}

.terminal-viewport :deep(.xterm) {
  height: 100%;
}

.terminal-viewport :deep(.xterm-viewport) {
  scrollbar-width: auto;
  padding-right: 12px;
}

.terminal-viewport :deep(.xterm-viewport::-webkit-scrollbar) {
  width: 18px;
  height: 18px;
}

.terminal-viewport :deep(.xterm-viewport::-webkit-scrollbar-track) {
  background: #2d2d2d;
}

.terminal-viewport :deep(.xterm-viewport::-webkit-scrollbar-thumb) {
  background: #5a5a5a;
  border-radius: 10px;
  border: 4px solid #2d2d2d;
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

  .toolbar-actions {
    margin-left: 0;
    width: 100%;
    justify-content: flex-end;
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

  .toolbar-actions {
    justify-content: stretch;
    gap: 6px;
  }

  .sidebar-toggle-btn {
    flex: 1 1 0;
    justify-content: center;
    min-width: 0;
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

  .sidebar-section,
  .file-browser-panel,
  .shortcut-panel,
  .recipes-panel,
  .terminal-viewport,
  .editor-viewport,
  .terminal-tabs,
  .terminal-list,
  .recipe-list,
  .file-list {
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

  .shortcut-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .shortcut-editor {
    grid-template-columns: 1fr;
  }

  .shortcut-editor-actions {
    flex-wrap: wrap;
  }

  .recipe-editor-actions,
  .editor-toolbar,
  .right-tab-footer {
    flex-wrap: wrap;
  }

  .editor-actions {
    width: 100%;
    justify-content: flex-end;
  }

  .file-item,
  .terminal-item,
  .recipe-item {
    margin-left: 6px;
    margin-right: 6px;
  }

  .folder-creator {
    align-items: stretch;
    flex-direction: column;
  }

  .folder-creator-actions {
    justify-content: flex-end;
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

  .section-header {
    padding-left: 12px;
    padding-right: 12px;
  }

  .terminal-item,
  .recipe-item,
  .file-item {
    padding-left: 10px;
    padding-right: 10px;
  }

  .tab-btn {
    padding: 4px 8px;
  }

  .path-breadcrumb {
    width: 100%;
    flex: 1 1 100%;
  }
}

.main.left-collapsed {
  grid-template-columns: minmax(520px, 1fr) minmax(260px, 320px);
}

.main.right-collapsed {
  grid-template-columns: minmax(230px, 320px) minmax(560px, 1fr);
}

.main.both-collapsed {
  grid-template-columns: minmax(0, 1fr);
}

@media (max-width: 1380px) {
  .main.left-collapsed {
    grid-template-columns: minmax(500px, 1fr) minmax(240px, 300px);
  }

  .main.right-collapsed {
    grid-template-columns: minmax(210px, 280px) minmax(500px, 1fr);
  }
}

@media (max-width: 980px) {
  .main.left-collapsed,
  .main.both-collapsed {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
