<template>
  <div class="mobile-page">
    <section class="panel mobile-header">
      <h1>WebCLI Mobile</h1>
      <div class="row">
        <RouterLink class="link-btn" to="/">Desktop</RouterLink>
        <RouterLink class="link-btn" to="/mobile/files">Files</RouterLink>
      </div>
    </section>

    <section class="panel">
      <label>
        Instance
        <select v-model="selectedId">
          <option value="">Select instance</option>
          <option v-for="item in terminalStore.instances" :key="item.id" :value="item.id">
            {{ item.node_name || item.node_id || 'node' }} | {{ item.cwd || '-' }} | {{ item.command }}
          </option>
        </select>
      </label>
      <div class="row">
        <button type="button" @click="refresh">Refresh</button>
        <button type="button" @click="connect" :disabled="!selectedId || terminalStore.isReconnecting">Connect</button>
        <button type="button" @click="disconnect" :disabled="!terminalStore.wsConnected">Disconnect</button>
      </div>
      <div class="subtle">{{ terminalStore.status }}</div>
    </section>

    <section class="panel terminal-mobile-panel">
      <div ref="terminalRef" class="terminal mobile-terminal" />
    </section>

    <section class="panel shortcuts">
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
  </div>
</template>

<script setup>
import { onBeforeUnmount, onMounted, ref } from 'vue';
import { Terminal } from 'xterm';
import { FitAddon } from '@xterm/addon-fit';
import 'xterm/css/xterm.css';
import { useWebCliTerminalStore } from '../stores/webcli-terminal.js';
import { createTerminalProtocolRenderer } from '../composables/useTerminalProtocol.js';

const terminalStore = useWebCliTerminalStore();
const terminalRef = ref(null);
const selectedId = ref('');

let term = null;
let fitAddon = null;
let unsubscribe = null;
let renderer = null;

function sendShortcut(value) {
  terminalStore.sendInput(value).finally(() => {
    term?.focus();
  });
}

async function refresh() {
  await Promise.all([
    terminalStore.fetchInstances(),
    terminalStore.fetchNodes()
  ]);
}

async function connect() {
  await terminalStore.connect(selectedId.value);
  term?.focus();
}

function disconnect() {
  terminalStore.disconnect();
}

onMounted(async () => {
  term = new Terminal({
    convertEol: true,
    fontSize: 14,
    scrollback: 2000,
    theme: {
      background: '#121923',
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
});

onBeforeUnmount(() => {
  unsubscribe?.();
  term?.dispose();
});
</script>
