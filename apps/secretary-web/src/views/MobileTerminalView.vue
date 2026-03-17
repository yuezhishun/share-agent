<template>
  <div class="mobile-page mobile-workbench">
    <section class="panel mobile-header">
      <h1>WebCLI Mobile</h1>
      <div class="row">
        <RouterLink class="link-btn" to="/">Desktop</RouterLink>
      </div>
    </section>

    <section class="panel">
      <label>
        Instance
        <select v-model="selectedId">
          <option value="">Select instance</option>
          <option v-for="item in terminalStore.instances" :key="item.id" :value="item.id">
            {{ formatInstanceOptionLabel(item) }}
          </option>
        </select>
      </label>
      <div class="row">
        <button type="button" @click="refresh">Refresh</button>
        <button type="button" @click="connect" :disabled="!selectedId || terminalStore.isReconnecting">Connect</button>
        <button type="button" @click="disconnect" :disabled="!terminalStore.wsConnected">Disconnect</button>
      </div>
      <div class="subtle">{{ terminalStore.status }}</div>
      <div class="row mobile-tabs">
        <button type="button" :class="{ active: currentView === 'terminal' }" @click="switchView('terminal')">终端</button>
        <button type="button" :class="{ active: currentView === 'files' }" @click="switchView('files')">文件</button>
        <button type="button" :class="{ active: currentView === 'recipes' }" @click="switchView('recipes')">配方</button>
      </div>
    </section>

    <section v-show="currentView === 'terminal'" class="panel terminal-mobile-panel">
      <div class="row">
        <button type="button" @click="pickUploadImage" :disabled="!terminalStore.selectedInstanceId">上传图片</button>
      </div>
      <div ref="terminalRef" class="terminal mobile-terminal" />
      <div class="row quick-row">
        <input
          v-model="quickCommand"
          placeholder="输入命令后回车执行"
          @keydown.enter.prevent="sendQuick"
        />
        <button type="button" @click="sendQuick">发送</button>
      </div>
      <section class="shortcuts">
        <div class="shortcut-grid">
          <button type="button" @click="sendShortcut('\u001b')">Esc</button>
          <button type="button" @click="sendShortcut('\t')">Tab</button>
          <button type="button" @click="sendShortcut('\r')">Enter</button>
          <button type="button" @click="sendShortcut('\u0003')">Ctrl+C</button>
          <button type="button" @click="sendShortcut('\u001b[A')">↑</button>
          <button type="button" @click="sendShortcut('\u001b[B')">↓</button>
          <button type="button" @click="sendShortcut('\u001b[D')">←</button>
          <button type="button" @click="sendShortcut('\u001b[C')">→</button>
        </div>
      </section>
    </section>

    <section v-show="currentView === 'files'" class="panel mobile-files">
      <div class="row">
        <button type="button" :disabled="!filesStore.parentPath || filesStore.loading" @click="goParent">Parent</button>
        <button type="button" :disabled="filesStore.loading" @click="reloadFiles">Refresh</button>
        <button type="button" :disabled="filesStore.actionLoading" @click="pickUploadFiles">Upload</button>
      </div>
      <div class="row">
        <button type="button" :disabled="filesStore.actionLoading" @click="toggleFolderCreator">
          {{ showFolderCreator ? 'Cancel' : 'New Folder' }}
        </button>
      </div>
      <form v-if="showFolderCreator" class="row" @submit.prevent="createFolder">
        <input v-model="folderName" placeholder="Folder name" />
        <button type="submit">Create</button>
      </form>
      <label class="checkbox-row">
        <input v-model="filesStore.showHidden" type="checkbox" @change="reloadFiles" /> Show hidden
      </label>
      <div class="subtle">{{ filesStore.currentPath }}</div>
      <div v-if="filesStore.error" class="error">{{ filesStore.error }}</div>
      <div v-if="filesStore.actionError" class="error">{{ filesStore.actionError }}</div>
      <ul class="files-list">
        <li v-for="item in filesStore.items" :key="item.path" class="file-item">
          <button type="button" @click="openFileEntry(item)">{{ item.kind }} · {{ item.name }}</button>
          <div class="row file-row-actions">
            <button type="button" @click="beginRename(item)">Rename</button>
            <button type="button" :disabled="item.kind !== 'file' && item.kind !== 'dir'" @click="downloadFile(item)">Download</button>
            <button type="button" @click="removeFile(item)">Delete</button>
            <button type="button" @click="insertPath(item.path)">Insert Path</button>
          </div>
          <form v-if="renamingPath === item.path" class="row" @submit.prevent="saveRename">
            <input v-model="renameValue" />
            <button type="submit">Save</button>
            <button type="button" @click="cancelRename">Cancel</button>
          </form>
        </li>
      </ul>
      <div v-if="filesStore.previewError" class="error">{{ filesStore.previewError }}</div>
      <pre v-if="filesStore.preview" class="preview">{{ filesStore.preview.content }}</pre>
    </section>

    <section v-show="currentView === 'recipes'" class="panel mobile-recipes">
      <form class="recipe-form" @submit.prevent="saveRecipe">
        <input v-model="recipeName" placeholder="Recipe name" />
        <input v-model="recipeGroup" placeholder="Group" />
        <textarea v-model="recipeCommand" rows="2" placeholder="Command"></textarea>
        <div class="row">
          <button type="submit">{{ editingRecipeId ? 'Update' : 'Save' }}</button>
          <button v-if="editingRecipeId" type="button" @click="cancelRecipeEdit">Cancel</button>
        </div>
      </form>
      <div class="recipe-groups">
        <div v-for="group in recipesStore.groups" :key="group.group" class="recipe-group">
          <div class="subtle">{{ group.group }}</div>
          <ul class="files-list">
            <li v-for="item in group.items" :key="item.id" class="file-item">
              <div>{{ item.name }}</div>
              <div class="subtle">{{ item.command }}</div>
              <div class="row file-row-actions">
                <button type="button" @click="runRecipe(item, true)">Run</button>
                <button type="button" @click="runRecipe(item, false)">Insert</button>
                <button type="button" @click="editRecipe(item)">Edit</button>
                <button type="button" @click="deleteRecipe(item.id)">Delete</button>
              </div>
            </li>
          </ul>
        </div>
      </div>
    </section>

    <input
      ref="uploadImageInputRef"
      class="hidden-input"
      type="file"
      accept="image/png,image/jpeg,image/webp,image/gif"
      @change="onUploadImageChange"
    />
    <input ref="uploadFilesInputRef" class="hidden-input" type="file" multiple @change="onUploadFilesChange" />
  </div>
</template>

<script setup>
import { nextTick, onBeforeUnmount, onMounted, ref } from 'vue';
import { RouterLink } from 'vue-router';
import { Terminal } from 'xterm';
import { FitAddon } from '@xterm/addon-fit';
import 'xterm/css/xterm.css';
import { useWebCliTerminalStore } from '../stores/webcli-terminal.js';
import { useWebCliFilesStore } from '../stores/webcli-files.js';
import { useWebCliRecipesStore } from '../stores/webcli-recipes.js';
import { createTerminalProtocolRenderer } from '../composables/useTerminalProtocol.js';

const terminalStore = useWebCliTerminalStore();
const filesStore = useWebCliFilesStore();
const recipesStore = useWebCliRecipesStore();

const terminalRef = ref(null);
const uploadImageInputRef = ref(null);
const uploadFilesInputRef = ref(null);
const selectedId = ref('');
const currentView = ref('terminal');
const quickCommand = ref('');
const showFolderCreator = ref(false);
const folderName = ref('');
const renamingPath = ref('');
const renameValue = ref('');
const recipeName = ref('');
const recipeCommand = ref('');
const recipeGroup = ref('general');
const editingRecipeId = ref('');

let term = null;
let fitAddon = null;
let unsubscribe = null;
let renderer = null;

function focusTerminal() {
  term?.focus();
}

function formatInstanceOptionLabel(instance) {
  const alias = terminalStore.getInstanceAlias(instance?.id);
  const cwd = String(instance?.cwd || '').trim() || '-';
  const command = String(instance?.command || '').trim() || 'bash';
  if (alias) {
    return `${alias} | ${cwd} | ${command}`;
  }
  const node = String(instance?.node_name || instance?.node_id || 'node').trim() || 'node';
  return `${node} | ${cwd} | ${command}`;
}

function sendShortcut(value) {
  terminalStore.sendInput(value).finally(() => {
    focusTerminal();
  });
}

function sendQuick() {
  const text = String(quickCommand.value || '').trim();
  if (!text) {
    return;
  }
  terminalStore.sendInput(`${text}\r`).finally(() => {
    focusTerminal();
  });
}

async function refresh() {
  await Promise.all([
    terminalStore.fetchInstances(),
    terminalStore.fetchNodes()
  ]);
  if (!selectedId.value && terminalStore.instances.length > 0) {
    selectedId.value = terminalStore.instances[0].id;
  }
}

async function connect() {
  await terminalStore.connect(selectedId.value);
  openSelectedCwd().catch(() => {});
  focusTerminal();
}

function disconnect() {
  terminalStore.disconnect();
}

async function openSelectedCwd() {
  const path = terminalStore.selectedInstance?.cwd || filesStore.currentPath;
  await filesStore.loadList(path);
}

function switchView(nextView) {
  currentView.value = nextView;
  if (nextView === 'terminal') {
    nextTick(() => {
      fitAddon?.fit();
      focusTerminal();
    });
    return;
  }
  if (nextView === 'files') {
    openSelectedCwd();
  }
}

function pickUploadImage() {
  uploadImageInputRef.value?.click();
}

async function onUploadImageChange(event) {
  const file = event?.target?.files?.[0];
  if (!file) {
    return;
  }
  try {
    await terminalStore.uploadImageToSelected(file, { pressEnter: true });
  } finally {
    if (event?.target) {
      event.target.value = '';
    }
    focusTerminal();
  }
}

function pickUploadFiles() {
  uploadFilesInputRef.value?.click();
}

async function onUploadFilesChange(event) {
  try {
    await filesStore.uploadFiles(event?.target?.files, filesStore.currentPath);
  } finally {
    if (event?.target) {
      event.target.value = '';
    }
  }
}

function goParent() {
  if (!filesStore.parentPath) {
    return;
  }
  filesStore.loadList(filesStore.parentPath);
}

function reloadFiles() {
  filesStore.loadList(filesStore.currentPath);
}

function openFileEntry(item) {
  filesStore.openEntry(item);
}

function toggleFolderCreator() {
  showFolderCreator.value = !showFolderCreator.value;
  folderName.value = '';
}

async function createFolder() {
  const name = String(folderName.value || '').trim();
  if (!name) {
    return;
  }
  await filesStore.createDirectory(name, filesStore.currentPath);
  showFolderCreator.value = false;
  folderName.value = '';
}

function beginRename(item) {
  renamingPath.value = item.path;
  renameValue.value = item.name;
}

function cancelRename() {
  renamingPath.value = '';
  renameValue.value = '';
}

async function saveRename() {
  if (!renamingPath.value) {
    return;
  }
  await filesStore.renameEntry(renamingPath.value, renameValue.value);
  cancelRename();
}

async function removeFile(item) {
  await filesStore.removeEntry(item.path, {
    recursive: item.kind === 'dir'
  });
}

function insertPath(path) {
  terminalStore.sendInput(path).finally(() => focusTerminal());
}

async function downloadFile(item) {
  if (item.kind !== 'file' && item.kind !== 'dir') {
    return;
  }
  await filesStore.downloadEntry(item.path);
}

function resetRecipeEditor() {
  editingRecipeId.value = '';
  recipeName.value = '';
  recipeCommand.value = '';
  recipeGroup.value = 'general';
}

function saveRecipe() {
  if (editingRecipeId.value) {
    recipesStore.updateRecipe(editingRecipeId.value, {
      name: recipeName.value,
      command: recipeCommand.value,
      group: recipeGroup.value
    });
  } else {
    recipesStore.addRecipe({
      name: recipeName.value,
      command: recipeCommand.value,
      group: recipeGroup.value
    });
  }
  resetRecipeEditor();
}

function editRecipe(item) {
  editingRecipeId.value = item.id;
  recipeName.value = item.name;
  recipeCommand.value = item.command;
  recipeGroup.value = item.group || 'general';
}

function cancelRecipeEdit() {
  resetRecipeEditor();
}

function deleteRecipe(id) {
  recipesStore.removeRecipe(id);
  if (editingRecipeId.value === id) {
    resetRecipeEditor();
  }
}

function runRecipe(item, pressEnter) {
  const args = Array.isArray(item?.args) ? item.args.map((x) => String(x)) : [];
  const env = item?.env && typeof item.env === 'object' && !Array.isArray(item.env) ? item.env : {};
  const cwdValue = String(item?.cwd || '').trim();
  const hasRuntimeConfig = args.length > 0 || cwdValue.length > 0 || Object.keys(env).length > 0;

  if (pressEnter && hasRuntimeConfig) {
    terminalStore.createInstance({
      command: String(item?.command || 'bash').trim() || 'bash',
      args,
      env,
      cols: 120,
      rows: 34,
      cwd: cwdValue || undefined
    }).then(async (created) => {
      if (created?.instance_id) {
        await terminalStore.fetchInstances();
        selectedId.value = created.instance_id;
        await connect();
      }
    }).finally(() => {
      switchView('terminal');
    });
    return;
  }

  const text = String(item?.command || '');
  terminalStore.sendInput(pressEnter ? `${text}\r` : text).finally(() => {
    switchView('terminal');
  });
}

onMounted(async () => {
  recipesStore.hydrate();

  term = new Terminal({
    convertEol: true,
    cursorBlink: true,
    fontSize: 14,
    scrollback: 3000,
    fontFamily: 'JetBrains Mono, monospace',
    theme: {
      background: '#0f1522',
      foreground: '#edf2f7'
    }
  });
  fitAddon = new FitAddon();
  term.loadAddon(fitAddon);
  term.open(terminalRef.value);
  fitAddon.fit();

  renderer = createTerminalProtocolRenderer(term);
  unsubscribe = terminalStore.subscribe((message) => {
    renderer.onMessage(message);
  });

  term.onData((data) => terminalStore.sendInput(data));
  term.onResize(({ cols, rows }) => terminalStore.sendResize(cols, rows));

  await refresh();
  if (selectedId.value) {
    await connect();
  }
});

onBeforeUnmount(() => {
  unsubscribe?.();
  term?.dispose();
});
</script>

<style scoped>
.mobile-workbench {
  gap: 10px;
  padding-bottom: 16px;
}

.mobile-tabs button.active {
  border-color: var(--accent);
  color: var(--accent);
}

.quick-row {
  margin-top: 8px;
}

.mobile-files,
.mobile-recipes {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.file-item {
  border: 1px solid var(--line);
  border-radius: 8px;
  padding: 8px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.file-row-actions {
  flex-wrap: wrap;
}

.recipe-form {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.recipe-groups {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.mobile-terminal {
  min-height: 48vh;
}
</style>
