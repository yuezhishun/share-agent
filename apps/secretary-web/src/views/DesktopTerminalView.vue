<template>
  <div class="page desktop-grid">
    <section class="panel controls">
      <h1>WebCLI Desktop</h1>
      <form class="form" @submit.prevent="createInstance">
        <label>
          Command
          <input v-model="command" required placeholder="bash" data-testid="command-input" />
        </label>
        <label>
          Args (JSON array)
          <input v-model="argsInput" placeholder='["-l"]' data-testid="args-input" />
        </label>
        <label>
          Project CWD
          <select v-model="projectPath" data-testid="project-select">
            <option value="">Default</option>
            <option v-for="item in terminalStore.projects" :key="item.path" :value="item.path">
              {{ item.name }} · {{ item.path }}
            </option>
          </select>
        </label>
        <label>
          Target Node
          <select v-model="createNodeId" data-testid="node-select">
            <option value="">Auto (master)</option>
            <option v-for="node in terminalStore.nodes" :key="node.node_id" :value="node.node_id">
              {{ node.node_name || node.node_id }} | {{ node.node_role || 'master' }} | {{ node.node_online ? 'online' : 'offline' }}
            </option>
          </select>
        </label>
        <label>
          CWD Override
          <input v-model="cwd" placeholder="/home/yueyuan/..." data-testid="cwd-input" />
        </label>
        <label>
          Env (JSON object)
          <input v-model="envInput" placeholder='{"KEY":"value"}' data-testid="env-input" />
        </label>
        <div class="row">
          <label>
            Cols
            <input v-model.number="cols" type="number" min="1" max="500" />
          </label>
          <label>
            Rows
            <input v-model.number="rows" type="number" min="1" max="300" />
          </label>
        </div>
        <button type="submit" data-testid="create-button">Create Instance</button>
      </form>

      <div class="toolbar row">
        <button type="button" @click="refresh">Refresh</button>
        <button type="button" @click="resync" :disabled="!terminalStore.wsConnected">Resync</button>
        <button type="button" @click="disconnect" :disabled="!terminalStore.wsConnected">Disconnect</button>
        <button type="button" @click="terminate" :disabled="!terminalStore.selectedInstanceId">Terminate</button>
        <button type="button" @click="pickUploadFile" :disabled="!terminalStore.selectedInstanceId">Upload Image</button>
        <select v-model="uploadMode" data-testid="upload-mode-select">
          <option value="insert">Insert Only</option>
          <option value="insert_enter">Insert + Enter</option>
        </select>
        <input ref="uploadInputRef" class="hidden-input" type="file" accept="image/png,image/jpeg,image/webp,image/gif" @change="onUploadFileChange" />
      </div>

      <div class="instances">
        <div class="instances-head">
          <h2>Instances</h2>
          <span class="badge">{{ terminalStore.instances.length }}</span>
        </div>
        <ul id="instance-list" data-testid="instance-list">
          <li v-for="item in terminalStore.instances" :key="item.id" :class="{ active: item.id === terminalStore.selectedInstanceId }">
            <div class="item-main">{{ item.cwd || '-' }} | {{ item.command }}</div>
            <div class="item-meta">
              {{ item.cols }}x{{ item.rows }} | {{ item.status }} |
              {{ item.node_name || item.node_id || 'unknown-node' }} | {{ item.node_role || 'master' }} |
              {{ item.node_online ? 'online' : 'offline' }}
              <span v-if="item.node_label"> | {{ item.node_label }}</span>
            </div>
            <button type="button" @click="connect(item.id)">Connect</button>
          </li>
        </ul>
      </div>
    </section>

    <section class="panel terminal-panel">
      <div class="terminal-head">
        <span data-testid="session-label">{{ sessionLabel }}</span>
        <span class="status" data-testid="status">{{ terminalStore.status }}</span>
      </div>
      <div ref="terminalRef" class="terminal" data-testid="terminal" />
      <pre class="plain-output" data-testid="plain-output">{{ plainOutput }}</pre>
    </section>

    <section class="panel files-panel">
      <div class="files-head">
        <h2>Files</h2>
        <button type="button" @click="openSelectedCwd">Open Instance CWD</button>
      </div>
      <div class="row">
        <button type="button" :disabled="!filesStore.parentPath || filesStore.loading" @click="goParent">Parent</button>
        <button type="button" :disabled="filesStore.loading" @click="reloadFiles">Refresh</button>
      </div>
      <label class="checkbox-row">
        <input v-model="filesStore.showHidden" type="checkbox" @change="reloadFiles" /> Show hidden
      </label>
      <div class="subtle">{{ filesStore.currentPath }}</div>
      <div v-if="filesStore.error" class="error">{{ filesStore.error }}</div>
      <ul class="files-list">
        <li v-for="item in filesStore.items" :key="item.path">
          <button type="button" @click="openEntry(item)">{{ item.kind }} · {{ item.name }}</button>
        </li>
      </ul>
      <div v-if="filesStore.previewError" class="error">{{ filesStore.previewError }}</div>
      <pre v-if="filesStore.preview" class="preview">{{ filesStore.preview.content }}</pre>
    </section>
  </div>
</template>

<script setup>
import { computed, onBeforeUnmount, onMounted, ref } from 'vue';
import { Terminal } from 'xterm';
import { FitAddon } from '@xterm/addon-fit';
import 'xterm/css/xterm.css';
import { useWebCliTerminalStore } from '../stores/webcli-terminal.js';
import { useWebCliFilesStore } from '../stores/webcli-files.js';
import { createTerminalProtocolRenderer } from '../composables/useTerminalProtocol.js';

const terminalStore = useWebCliTerminalStore();
const filesStore = useWebCliFilesStore();

const terminalRef = ref(null);
const uploadInputRef = ref(null);
const command = ref('bash');
const argsInput = ref('["-l"]');
const envInput = ref('');
const cwd = ref('');
const projectPath = ref('');
const createNodeId = ref('');
const uploadMode = ref('insert');
const cols = ref(100);
const rows = ref(32);
const plainOutput = ref('');

let term = null;
let fitAddon = null;
let unsubscribe = null;
let renderer = null;

const sessionLabel = computed(() => {
  if (!terminalStore.selectedInstanceId) {
    return 'No instance selected';
  }
  const selected = terminalStore.selectedInstance;
  const nodeName = selected?.node_name || selected?.node_id || 'unknown-node';
  return `Operating: ${nodeName}/${terminalStore.selectedInstanceId}`;
});

function parseJsonOrDefault(input, fallback) {
  const text = String(input || '').trim();
  if (!text) {
    return fallback;
  }
  return JSON.parse(text);
}

function refreshPlainOutput() {
  const merged = renderer.state.historyRows.concat(renderer.state.visibleRows);
  plainOutput.value = merged.map((line) => (line || []).map((seg) => Array.isArray(seg) ? seg[0] || '' : '').join('')).join('\n');
}

async function refresh() {
  await Promise.all([
    terminalStore.fetchInstances(),
    terminalStore.fetchNodes(),
    terminalStore.fetchProjects()
  ]);
}

async function connect(instanceId) {
  await terminalStore.connect(instanceId);
  focusTerminal();
}

function disconnect() {
  terminalStore.disconnect();
}

async function resync() {
  await terminalStore.resync();
  focusTerminal();
}

async function terminate() {
  await terminalStore.terminateSelected();
}

async function createInstance() {
  const args = parseJsonOrDefault(argsInput.value, []);
  const env = parseJsonOrDefault(envInput.value, {});
  const resolvedCwd = String(cwd.value || projectPath.value || '').trim();
  const created = await terminalStore.createInstance({
    command: command.value,
    args,
    env,
    cols: cols.value,
    rows: rows.value,
    cwd: resolvedCwd || undefined
  }, createNodeId.value);

  if (created?.instance_id) {
    await terminalStore.connect(created.instance_id);
    focusTerminal();
  }
}

function focusTerminal() {
  term?.focus();
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

function pickUploadFile() {
  uploadInputRef.value?.click();
}

async function onUploadFileChange(event) {
  const files = event?.target?.files;
  const file = files && files.length > 0 ? files[0] : null;
  if (!file) {
    return;
  }
  try {
    await terminalStore.uploadImageToSelected(file, { pressEnter: uploadMode.value === 'insert_enter' });
  } finally {
    if (event?.target) {
      event.target.value = '';
    }
    focusTerminal();
  }
}

function openSelectedCwd() {
  const path = terminalStore.selectedInstance?.cwd || filesStore.basePath;
  filesStore.loadList(path);
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

function openEntry(item) {
  filesStore.openEntry(item);
}

onMounted(async () => {
  term = new Terminal({
    convertEol: true,
    fontFamily: 'JetBrains Mono, monospace',
    fontSize: 13,
    scrollback: 3000,
    theme: {
      background: '#131a24',
      foreground: '#e6edf3'
    }
  });

  fitAddon = new FitAddon();
  term.loadAddon(fitAddon);
  term.open(terminalRef.value);
  fitAddon.fit();

  renderer = createTerminalProtocolRenderer(term);

  unsubscribe = terminalStore.subscribe((message) => {
    renderer.onMessage(message);
    refreshPlainOutput();
  });

  term.onData((data) => {
    terminalStore.sendInput(data);
  });

  term.onResize(({ cols: c, rows: r }) => {
    terminalStore.sendResize(c, r);
  });

  terminalRef.value?.addEventListener('paste', onTerminalPaste);

  await refresh();
  await filesStore.loadList(filesStore.currentPath);
  focusTerminal();
});

onBeforeUnmount(() => {
  unsubscribe?.();
  terminalStore.disconnect();
  terminalRef.value?.removeEventListener('paste', onTerminalPaste);
  term?.dispose();
});
</script>
